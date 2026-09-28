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
/// Email verification end to end — testing standard. A real PostgreSQL in a container, because the
/// single-use guarantee is the <c>consumed_at_utc IS NULL</c> predicate in the database and the
/// in-memory provider would not enforce it (ADR-0012).
/// </summary>
public sealed class EmailVerificationEndpointTests : IAsyncLifetime
{
    internal const string VerificationsPath = "/api/v1/auth/email-verifications";

    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
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
            TestJwtSettings.Apply(webHost);
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

    /// <summary>The happy path: register, read the token from the mail, spend it — subject V.1.</summary>
    [Fact]
    public async Task Post_WithTheIssuedToken_Returns204AndVerifiesTheAccountAsync()
    {
        // Arrange
        string token = await RegisterAndCaptureTokenAsync("verified@example.com");

        // Act
        using HttpResponseMessage response = await PostVerificationAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsEmailVerifiedAsync("verified@example.com"));
    }

    /// <summary>A replayed link is refused, and the account it already verified is untouched.</summary>
    [Fact]
    public async Task Post_ReplayingASpentToken_Returns400AndLeavesTheAccountVerifiedAsync()
    {
        // Arrange
        const string email = "replay@example.com";
        string token = await RegisterAndCaptureTokenAsync(email);
        using HttpResponseMessage first = await PostVerificationAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Act
        using HttpResponseMessage second = await PostVerificationAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.True(await IsEmailVerifiedAsync(email));
    }

    /// <summary>An unknown token is answered exactly like a spent one — no enumeration oracle.</summary>
    [Fact]
    public async Task Post_WithAnUnknownToken_Returns400Async()
    {
        // Arrange — the shape the generator produces, a value it never produced.
        string token = new('a', 43);

        // Act
        using HttpResponseMessage response = await PostVerificationAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Expiry is judged at the point of use, with no sweeper running — ADR-0024.</summary>
    [Fact]
    public async Task Post_WithAnExpiredToken_Returns410Async()
    {
        // Arrange
        const string email = "expired@example.com";
        string token = await RegisterAndCaptureTokenAsync(email);
        await ExpireTokensOfAsync(email);

        // Act
        using HttpResponseMessage response = await PostVerificationAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.False(await IsEmailVerifiedAsync(email));
    }

    /// <summary>
    /// The concurrency case AGENTS.md §5 requires: the requests run in parallel and the assertion is
    /// on the final database state. One 204, one <c>consumed_at_utc</c>, nineteen refusals.
    /// </summary>
    [Fact]
    public async Task Post_TwentyConcurrentVerificationsOfOneToken_VerifyExactlyOnceAsync()
    {
        // Arrange
        const string email = "contended-verify@example.com";
        const int callers = 20;
        string token = await RegisterAndCaptureTokenAsync(email);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => PostVerificationAsync(token)));

        try
        {
            // Assert
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(callers - 1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
            Assert.Equal(1, await CountConsumedTokensAsync(email));
            Assert.True(await IsEmailVerifiedAsync(email));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>
    /// A duplicate registration sends nothing — SDD §10.1 step 5. A second "you just registered"
    /// mail would make the mailbox an enumeration oracle with a delivery receipt.
    /// </summary>
    [Fact]
    public async Task Post_ADuplicateRegistration_SendsNoSecondMailAsync()
    {
        // Arrange
        const string email = "duplicate-mail@example.com";
        _ = await RegisterAndCaptureTokenAsync(email);
        int sentAfterFirst = _mail.Tokens.Count;

        // Act
        using HttpResponseMessage second = await PostRegistrationAsync(email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(sentAfterFirst, _mail.Tokens.Count);
    }

    private async Task<string> RegisterAndCaptureTokenAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpResponseMessage response = await PostRegistrationAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        IReadOnlyList<string> tokens = _mail.Tokens;
        Assert.Equal(before + 1, tokens.Count);

        return tokens[^1];
    }

    private async Task<HttpResponseMessage> PostRegistrationAsync(string email)
    {
        using HttpClient client = _factory.CreateClient();

        return await client.PostAsJsonAsync(
            RegistrationPath,
            new { email, password = AccountPassword, displayName = "Player One" },
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

    private async Task<int> CountConsumedTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.EmailVerificationTokens
            .AsNoTracking()
            .CountAsync(
                token => token.ConsumedAtUtc != null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task ExpireTokensOfAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        // Reaching past the API on purpose: nothing in the contract can age a token, and waiting out
        // a 24-hour window is not a test.
        await context.EmailVerificationTokens
            .Where(token => context.Users.Any(user => user.Id == token.UserId && user.Email == email))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ExpiresAtUtc, DateTime.UtcNow.AddHours(-1)),
                TestContext.Current.CancellationToken);
    }
}
