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
/// The typed half of the password reset — SPEC-055. A real PostgreSQL, because single use and the
/// attempt budget are both database predicates (ADR-0012).
/// </summary>
/// <remarks>
/// Its own suite rather than more cases in <see cref="PasswordResetEndpointTests"/>, which stands at the
/// file length the root .editorconfig allows.
/// </remarks>
public sealed class PasswordResetCodeEndpointTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string NewPassword = "a brand new passphrase";

    /// <summary>What the row allows before it is spent — the thing that makes six characters safe.</summary>
    internal const int Budget = 5;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender, and brings the schema up.</summary>
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

    /// <summary>The code replaces the password, exactly as the link does — SPEC-055.</summary>
    [Fact]
    public async Task Reset_WithTheCode_Returns204AndReplacesThePasswordAsync()
    {
        // Arrange
        const string email = "code-reset@example.com";
        await RegisterAndVerifyAsync(email);
        string code = await RequestResetAsync(email);

        // Act
        using HttpResponseMessage reset = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { email, code, newPassword = NewPassword });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using HttpResponseMessage withNew = await PostLoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        using HttpResponseMessage withOld = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    /// <summary>The code spends the same row the link would, so the link stops working too.</summary>
    /// <remarks>
    /// One row, two credentials — SPEC-055 decision 1. A mail that handed out two independent takeovers
    /// would be a mail worth stealing twice.
    /// </remarks>
    [Fact]
    public async Task Reset_WithTheCode_AlsoSpendsTheLinkFromTheSameMailAsync()
    {
        // Arrange
        const string email = "one-row@example.com";
        await RegisterAndVerifyAsync(email);
        string code = await RequestResetAsync(email);
        string token = _mail.ResetTokens[^1];

        using HttpResponseMessage first = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { email, code, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Act — the link out of the same message, with a password the replayer chose.
        using HttpResponseMessage second = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { token, newPassword = "the attackers passphrase" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        using HttpResponseMessage signedIn = await PostLoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>Five wrong codes spend the row, and the sixth attempt fails though it is right.</summary>
    /// <remarks>
    /// The budget is the whole security argument for six characters in front of the strongest action in
    /// the system: without it, 1.07e9 combinations and free guesses is not a credential.
    /// </remarks>
    [Fact]
    public async Task Reset_AfterFiveWrongCodes_RefusesTheRightOneAsync()
    {
        // Arrange
        const string email = "budget-spent@example.com";
        await RegisterAndVerifyAsync(email);
        string code = await RequestResetAsync(email);

        for (int attempt = 0; attempt < Budget; attempt++)
        {
            using HttpResponseMessage wrong = await PostAsync(
                PasswordResetEndpointTests.ResetsPath,
                new { email, code = "ZZZZZZ", newPassword = NewPassword });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        // Act — the code from the mail, which is right and arrives too late.
        using HttpResponseMessage right = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { email, code, newPassword = NewPassword });

        // Assert — refused, and the old password still works.
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);

        using HttpResponseMessage signedIn = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>A code for an address nobody registered answers exactly like a wrong one.</summary>
    /// <remarks>
    /// The whole point of the shared answer: a caller who could tell them apart could ask this endpoint
    /// which addresses hold an account.
    /// </remarks>
    [Fact]
    public async Task Reset_WithACodeForAnUnknownAddress_AnswersLikeAWrongCodeAsync()
    {
        // Arrange
        const string email = "known-for-comparison@example.com";
        await RegisterAndVerifyAsync(email);
        await RequestResetAsync(email);

        using HttpResponseMessage wrongCode = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { email, code = "ZZZZZZ", newPassword = NewPassword });

        // Act
        using HttpResponseMessage unknownAddress = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { email = "nobody-at-all@example.com", code = "ZZZZZZ", newPassword = NewPassword });

        // Assert — same status, same body.
        Assert.Equal(wrongCode.StatusCode, unknownAddress.StatusCode);
        Assert.Equal(
            await wrongCode.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await unknownAddress.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Twenty callers racing with one code produce one reset — the parallel case AGENTS.md requires of
    /// every contended write.
    /// </summary>
    [Fact]
    public async Task Reset_TwentyParallelCodes_SpendTheRowOnceAsync()
    {
        // Arrange
        const string email = "raced-code@example.com";
        const int callers = 20;
        await RegisterAndVerifyAsync(email);
        string code = await RequestResetAsync(email);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => PostAsync(
                PasswordResetEndpointTests.ResetsPath,
                new { email, code, newPassword = NewPassword })));

        try
        {
            // Assert — the database arbitrates, so exactly one caller spent the row.
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(1, await CountConsumedResetTokensAsync(email));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task RegisterAndVerifyAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpResponseMessage registered = await PostAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(before + 1, _mail.Tokens.Count);

        using HttpResponseMessage verified = await PostAsync(
            LoginEndpointTests.VerificationsPath,
            new { token = _mail.Tokens[^1] });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    /// <summary>Asks for a reset and returns the code the mail carried.</summary>
    private async Task<string> RequestResetAsync(string email)
    {
        int before = _mail.ResetCodes.Count;

        using HttpResponseMessage response = await PostAsync(
            PasswordResetEndpointTests.RequestsPath,
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, _mail.ResetCodes.Count);

        return _mail.ResetCodes[^1];
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string password) =>
        await PostAsync(LoginEndpointTests.SessionsPath, new { email, password });

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountConsumedResetTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.PasswordResetTokens
            .AsNoTracking()
            .CountAsync(
                token => token.ConsumedAtUtc != null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }
}
