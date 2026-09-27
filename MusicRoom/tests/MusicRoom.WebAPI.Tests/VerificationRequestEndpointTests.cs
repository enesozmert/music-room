using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Verification resend end to end — testing standard. A real PostgreSQL in a container, because
/// superseding is one transaction over two statements and the in-memory provider would not run it
/// as one (ADR-0012). <c>IMailService</c> is recorded rather than logged, so the assertion is "how
/// many messages, carrying which token" instead of a log scrape.
/// </summary>
/// <remarks>
/// Every test method carries the <c>Async</c> suffix the root <c>.editorconfig</c> requires, so the
/// names differ from SPEC-004's Verification table by that suffix only.
/// </remarks>
public sealed class VerificationRequestEndpointTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string VerificationRequestsPath = "/api/v1/auth/verification-requests";
    internal const string VerificationsPath = "/api/v1/auth/email-verifications";
    internal const string RegistrationPath = "/api/v1/auth/registrations";
    internal const string AccountPassword = "correct horse battery";

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

    /// <summary>
    /// Subject V.1: an account whose first mail never arrived can ask for another link, and the
    /// link it gets verifies the address.
    /// </summary>
    [Fact]
    public async Task UnverifiedAccount_ReceivesASecondLinkAsync()
    {
        // Arrange
        const string email = "resend-works@example.com";
        _ = await RegisterAndCaptureTokenAsync(email);
        int sentAfterRegistration = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage response = await PostVerificationRequestAsync(email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sentAfterRegistration + 1, _mail.Tokens.Count);

        using HttpResponseMessage verification = await PostVerificationAsync(_mail.Tokens[^1]);
        Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);
        Assert.True(await IsEmailVerifiedAsync(email));
    }

    /// <summary>
    /// Owner decision 1 of 2026-09-27 as implemented: a resend supersedes the outstanding link, so
    /// exactly one is live and the guess surface does not grow with the number of resends.
    /// </summary>
    [Fact]
    public async Task Resend_InvalidatesThePreviousLinkAsync()
    {
        // Arrange
        const string email = "supersede@example.com";
        string firstToken = await RegisterAndCaptureTokenAsync(email);

        using HttpResponseMessage resend = await PostVerificationRequestAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        string secondToken = _mail.Tokens[^1];
        Assert.NotEqual(firstToken, secondToken);

        // Act — the link from the first mail, after the second was issued.
        using HttpResponseMessage replayed = await PostVerificationAsync(firstToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.False(await IsEmailVerifiedAsync(email));

        using HttpResponseMessage newest = await PostVerificationAsync(secondToken);
        Assert.Equal(HttpStatusCode.NoContent, newest.StatusCode);
        Assert.True(await IsEmailVerifiedAsync(email));
    }

    /// <summary>
    /// Subject V.6: the endpoint is not an account-enumeration oracle. An address nobody registered
    /// is answered exactly like one that exists, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task UnknownAddress_Answers202AndSendsNothingAsync()
    {
        // Arrange
        int sentBefore = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage response = await PostVerificationRequestAsync("nobody@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sentBefore, _mail.Tokens.Count);
    }

    /// <summary>
    /// A verified account cannot be mailed on an anonymous caller's demand, and the caller cannot
    /// tell that from success — owner decision 3 of 2026-09-27.
    /// </summary>
    [Fact]
    public async Task VerifiedAccount_Answers202AndSendsNothingAsync()
    {
        // Arrange
        const string email = "already-verified@example.com";
        string token = await RegisterAndCaptureTokenAsync(email);
        using HttpResponseMessage verification = await PostVerificationAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);
        int sentBefore = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage response = await PostVerificationRequestAsync(email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sentBefore, _mail.Tokens.Count);
    }

    /// <summary>
    /// The address is matched the way registration stored it, so a user who capitalises their
    /// address in the resend form still gets their link.
    /// </summary>
    [Fact]
    public async Task MixedCaseAddress_ResolvesToTheRegisteredAccountAsync()
    {
        // Arrange
        const string email = "mixed-case@example.com";
        _ = await RegisterAndCaptureTokenAsync(email);
        int sentBefore = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage response = await PostVerificationRequestAsync("Mixed-Case@Example.COM");

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sentBefore + 1, _mail.Tokens.Count);
    }

    /// <summary>
    /// A malformed address is refused at the validator, with the 422 registration answers. The
    /// failure is about the shape of the input, which tells a caller nothing about who is
    /// registered.
    /// </summary>
    [Fact]
    public async Task MalformedAddress_Returns422AndSendsNothingAsync()
    {
        // Arrange
        int sentBefore = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage response = await PostVerificationRequestAsync("not-an-address");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(sentBefore, _mail.Tokens.Count);
    }

    private async Task<string> RegisterAndCaptureTokenAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationPath,
            new { email, password = AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        IReadOnlyList<string> tokens = _mail.Tokens;
        Assert.Equal(before + 1, tokens.Count);

        return tokens[^1];
    }

    private async Task<HttpResponseMessage> PostVerificationRequestAsync(string email)
    {
        using HttpClient client = _factory.CreateClient();

        return await client.PostAsJsonAsync(
            VerificationRequestsPath,
            new { email },
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostVerificationAsync(string token)
    {
        using HttpClient client = _factory.CreateClient();

        return await client.PostAsJsonAsync(
            VerificationsPath,
            new { token },
            TestContext.Current.CancellationToken);
    }

    private async Task<bool> IsEmailVerifiedAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        User user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == email, TestContext.Current.CancellationToken);

        return user.IsEmailVerified;
    }
}
