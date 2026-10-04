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
/// Refresh rotation end to end — testing standard. A real PostgreSQL in a container, because single
/// use is the <c>consumed_at_utc IS NULL</c> predicate in the database and the in-memory provider
/// would not arbitrate it (ADR-0012).
/// </summary>
public sealed class SessionRotationEndpointTests : IAsyncLifetime
{
    internal const string TokensPath = "/api/v1/auth/tokens";

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

    /// <summary>
    /// A live token is exchanged once: the caller gets a new pair and the presented value stops
    /// working — the rotation note in <c>docs/api/ENDPOINTS.md</c> §1.
    /// </summary>
    [Fact]
    public async Task Rotate_WithValidRefreshToken_ReturnsRotatedPairAndTheOldTokenStopsWorkingAsync()
    {
        // Arrange
        TokenPair first = await SignInAsync("rotates@example.com");

        // Act
        using HttpResponseMessage rotated = await PostRotationAsync(first.RefreshToken);
        TokenPair? second = await rotated.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        using HttpResponseMessage replayed = await PostRotationAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    /// <summary>
    /// Replaying a rotated token is theft, not a mistake: the whole family is revoked, so the
    /// replacement the real client holds stops working too — SDD §13.1.
    /// </summary>
    [Fact]
    public async Task Rotate_WithAlreadyRotatedToken_Returns401AndRevokesTheFamilyAsync()
    {
        // Arrange
        const string email = "theft@example.com";
        TokenPair first = await SignInAsync(email);

        using HttpResponseMessage rotated = await PostRotationAsync(first.RefreshToken);
        TokenPair? second = await rotated.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(second);

        // Act — the leaked, already-spent value is presented again.
        using HttpResponseMessage replayed = await PostRotationAsync(first.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        Assert.Equal(0, await CountLiveTokensAsync(email));

        // The replacement was live before the replay and is revoked with the rest of its family.
        using HttpResponseMessage afterRevocation = await PostRotationAsync(second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevocation.StatusCode);
    }

    /// <summary>Expiry is judged at the point of use, with no sweeper running — ADR-0024.</summary>
    [Fact]
    public async Task Rotate_WithExpiredToken_Returns401Async()
    {
        // Arrange
        const string email = "expired-session@example.com";
        TokenPair pair = await SignInAsync(email);
        await ExpireTokensOfAsync(email);

        // Act
        using HttpResponseMessage response = await PostRotationAsync(pair.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An unknown token answers exactly as an expired one — no enumeration oracle.</summary>
    [Fact]
    public async Task Rotate_WithAnUnknownToken_Returns401Async()
    {
        // Arrange — the shape the generator produces, a value it never produced.
        string token = new('a', 43);

        // Act
        using HttpResponseMessage response = await PostRotationAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The concurrency case AGENTS.md §5 requires: the requests run in parallel and the assertion is
    /// on the final database state. One 200, one spent row, one replacement — and because nineteen
    /// callers presented a token that was already spent by the time they reached the row, the family
    /// is revoked, which is the honest outcome of a token being used twice.
    /// </summary>
    [Fact]
    public async Task Rotate_TwentyParallelRotationsOfOneToken_ProduceExactlyOneSuccessAsync()
    {
        // Arrange
        const string email = "contended-rotation@example.com";
        const int callers = 20;
        TokenPair pair = await SignInAsync(email);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => PostRotationAsync(pair.RefreshToken)));

        try
        {
            // Assert
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(
                callers - 1,
                responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
            Assert.Equal(1, await CountConsumedTokensAsync(email));

            // The first token plus its single replacement, and nothing else.
            Assert.Equal(2, await CountTokensAsync(email));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task<TokenPair> SignInAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpClient client = _factory.CreateAuditedClient();
        using HttpResponseMessage registered = await client.PostAsJsonAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(before + 1, _mail.Tokens.Count);

        using HttpResponseMessage verified = await client.PostAsJsonAsync(
            LoginEndpointTests.VerificationsPath,
            new { token = _mail.Tokens[^1] },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        using HttpResponseMessage signedIn = await client.PostAsJsonAsync(
            LoginEndpointTests.SessionsPath,
            new { email, password = LoginEndpointTests.AccountPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        TokenPair? pair = await signedIn.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(pair);

        return pair;
    }

    private async Task<HttpResponseMessage> PostRotationAsync(string refreshToken)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            TokensPath,
            new { refreshToken },
            TestContext.Current.CancellationToken);
    }

    private async Task<int> CountTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .CountAsync(
                token => context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task<int> CountConsumedTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .CountAsync(
                token => token.ConsumedAtUtc != null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task<int> CountLiveTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .CountAsync(
                token => token.ConsumedAtUtc == null
                    && token.RevokedAtUtc == null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task ExpireTokensOfAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        // Reaching past the API on purpose: nothing in the contract can age a token, and waiting out
        // a 30-day window is not a test.
        await context.RefreshTokens
            .Where(token => context.Users.Any(user => user.Id == token.UserId && user.Email == email))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)),
                TestContext.Current.CancellationToken);
    }
}
