using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Verifying by code, and the budget that makes a six-character code safe — SPEC-052.
/// </summary>
/// <remarks>
/// The link half is covered by <see cref="EmailVerificationEndpointTests" />, which this leaves alone: what
/// is new here is a credential somebody can guess, so every test is about what happens when they try.
/// </remarks>
public sealed class EmailVerificationCodeTests : IAsyncLifetime
{
    private const string _registrations = "/api/v1/auth/registrations";
    private const string _verifications = "/api/v1/auth/email-verifications";
    private const string _password = "correct-horse-battery-staple-42";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, substitutes the mail sender, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>A correct code verifies the address, once.</summary>
    [Fact]
    public async Task ACorrectCode_VerifiesTheAddressOnceAsync()
    {
        // Arrange
        const string email = "code-happy@example.com";
        string code = await RegisterAsync(email);

        // Act
        using HttpResponseMessage verified = await SubmitAsync(email, code);
        using HttpResponseMessage again = await SubmitAsync(email, code);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    /// <summary>
    /// Five wrong codes spend the row, and the sixth attempt is refused **even when it is right**.
    /// </summary>
    /// <remarks>
    /// The assertion the whole design rests on. Without it a six-character code is one in a billion per
    /// guess and guesses are free, which is a credential an attacker walks through at their leisure.
    /// </remarks>
    [Fact]
    public async Task FiveWrongCodes_SpendTheRowEvenForTheRightOneAsync()
    {
        // Arrange
        const string email = "code-budget@example.com";
        string code = await RegisterAsync(email);

        // Act — five guesses, none of them the real code.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage wrong = await SubmitAsync(email, "ZZZZZZ");

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using HttpResponseMessage correct = await SubmitAsync(email, code);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, correct.StatusCode);

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.User user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == email, TestContext.Current.CancellationToken);

        Assert.False(user.IsEmailVerified);
    }

    /// <summary>A code for one address does not verify another.</summary>
    /// <remarks>
    /// Six characters are not unique across accounts, which is why the address is part of the lookup. A
    /// code that worked on any account would be a billion-to-one guess against **every** account at once.
    /// </remarks>
    [Fact]
    public async Task ACodeForOneAddress_DoesNotVerifyAnotherAsync()
    {
        string code = await RegisterAsync("code-mine@example.com");
        await RegisterAsync("code-yours@example.com");

        using HttpResponseMessage crossed = await SubmitAsync("code-yours@example.com", code);

        Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
    }

    /// <summary>An address nobody registered answers exactly as a wrong code does.</summary>
    [Fact]
    public async Task AnUnknownAddress_AnswersLikeAWrongCodeAsync()
    {
        using HttpResponseMessage unknown = await SubmitAsync("nobody@example.com", "ABC234");

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    /// <summary>Case and spacing do not matter: what a mail client pastes still works.</summary>
    [Fact]
    public async Task ACodeWithSpacesAndLowerCase_StillVerifiesAsync()
    {
        const string email = "code-pasted@example.com";
        string code = await RegisterAsync(email);

        string pasted = $" {code[..3].ToLowerInvariant()}-{code[3..].ToLowerInvariant()} ";

        using HttpResponseMessage verified = await SubmitAsync(email, pasted);

        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    /// <summary>The link still works, and still carries its own 256-bit token.</summary>
    /// <remarks>
    /// The code is a second credential, not a replacement. If the link had been reduced to the code,
    /// this is the test that would say so.
    /// </remarks>
    [Fact]
    public async Task TheLink_StillWorksAndIsStillLongAsync()
    {
        const string email = "code-and-link@example.com";
        await RegisterAsync(email);

        string token = _mail.Tokens[^1];

        Assert.Equal(43, token.Length);

        using HttpClient client = _factory.CreateAuditedClient();
        using HttpResponseMessage verified = await client.PostAsJsonAsync(
            _verifications,
            new { token },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    /// <summary>A Turkish request gets a Turkish message — SPEC-052 decision 6.</summary>
    [Fact]
    public async Task AcceptLanguageTurkish_SendsATurkishMessageAsync()
    {
        using HttpClient client = _factory.CreateAuditedClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en;q=0.8");

        using HttpResponseMessage registered = await client.PostAsJsonAsync(
            _registrations,
            new { email = "code-turkish@example.com", password = _password, displayName = "Enes" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(MailLanguage.Turkish, _mail.Verifications[^1].Language);
    }

    /// <summary>Anything else gets English, including a header nobody sent.</summary>
    [Fact]
    public async Task AnyOtherLanguage_GetsEnglishAsync()
    {
        using HttpClient client = _factory.CreateAuditedClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr-FR,fr;q=0.9");

        using HttpResponseMessage registered = await client.PostAsJsonAsync(
            _registrations,
            new { email = "code-french@example.com", password = _password, displayName = "Reader" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(MailLanguage.English, _mail.Verifications[^1].Language);
    }

    /// <summary>The welcome message is sent once, on the first sign-in, and never again.</summary>
    /// <remarks>
    /// "Once" is a conditional UPDATE rather than a read: two sign-ins that race both run it and exactly
    /// one affects a row. This proves the sequential half, which is the half a person experiences.
    /// </remarks>
    [Fact]
    public async Task TheWelcomeMessage_IsSentOnceOnTheFirstSignInAsync()
    {
        // Arrange — register, verify by code, then sign in twice.
        const string email = "code-welcome@example.com";
        string code = await RegisterAsync(email);

        using HttpResponseMessage verified = await SubmitAsync(email, code);
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        Assert.Empty(_mail.Welcomes);

        // Act
        await SignInAsync(email);
        await SignInAsync(email);

        // Assert
        WelcomeMail welcome = Assert.Single(_mail.Welcomes);

        Assert.Equal(email, welcome.EmailAddress);
        Assert.Equal("Reader", welcome.DisplayName);
    }

    private async Task SignInAsync(string email)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        using HttpResponseMessage signedIn = await client.PostAsJsonAsync(
            "/api/v1/auth/sessions",
            new { email, password = _password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>Registers an address and returns the code the message carried.</summary>
    private async Task<string> RegisterAsync(string email)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        using HttpResponseMessage registered = await client.PostAsJsonAsync(
            _registrations,
            new { email, password = _password, displayName = "Reader" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        return _mail.Codes[^1];
    }

    private async Task<HttpResponseMessage> SubmitAsync(string email, string code)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            _verifications,
            new { email, code },
            TestContext.Current.CancellationToken);
    }
}
