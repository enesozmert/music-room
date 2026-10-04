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
/// The authenticated session endpoints — SPEC-010, subject V.1 and V.6.
/// </summary>
/// <remarks>
/// The first suite in this codebase that authenticates. Its tokens come from real sign-ins, so a broken issuer
/// or a broken validator both fail here rather than cancelling out.
/// </remarks>
public sealed class SessionEndpointTests : IAsyncLifetime
{
    internal const string SessionsPath = "/api/v1/auth/sessions";
    internal const string CurrentSessionPath = "/api/v1/auth/sessions/current";

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

    /// <summary>Logout ends this session and leaves the account's other one alone — escalation 1.</summary>
    [Fact]
    public async Task Logout_EndsTheCurrentSessionOnlyAsync()
    {
        // Arrange — two sessions for one account.
        const string email = "two-sessions@example.com";
        (HttpClient client, TokenPair first) = await TestBearer.SignInAsync(_factory, _mail, email);
        TokenPair second = await TestBearer.SignInAgainAsync(_factory, email);

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.DeleteAsync(
            CurrentSessionPath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The ended session cannot rotate; the other one still can.
        using HttpResponseMessage endedRotation = await PostRotationAsync(first.RefreshToken);
        using HttpResponseMessage liveRotation = await PostRotationAsync(second.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, endedRotation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveRotation.StatusCode);
    }

    /// <summary>Logout is server-side: a kept copy of the refresh token is worthless afterwards.</summary>
    [Fact]
    public async Task Logout_ThenRotatingTheEndedSession_Returns401Async()
    {
        // Arrange
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(
            _factory,
            _mail,
            "logout-then-rotate@example.com");

        using HttpClient authenticated = client;
        using HttpResponseMessage loggedOut = await authenticated.DeleteAsync(
            CurrentSessionPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, loggedOut.StatusCode);

        // Act
        using HttpResponseMessage rotation = await PostRotationAsync(pair.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, rotation.StatusCode);
    }

    /// <summary>The list names each session's device, and flags nothing it should not carry.</summary>
    [Fact]
    public async Task ListSessions_ReturnsTheCallersOwnSessionsAsync()
    {
        // Arrange
        const string email = "lists-sessions@example.com";
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);
        _ = await TestBearer.SignInAgainAsync(_factory, email);

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.GetAsync(
            SessionsPath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<SessionRow>? sessions = await response.Content.ReadFromJsonAsync<List<SessionRow>>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(sessions);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session =>
        {
            Assert.Equal(TestClientContext.Device, session.Device);
            Assert.Equal(TestClientContext.Platform, session.Platform);
            Assert.NotEqual(Guid.Empty, session.SessionId);
        });
    }

    /// <summary>The body carries no token and no digest — §13.5.</summary>
    [Fact]
    public async Task ListSessions_ReturnsNoTokenOrDigestAsync()
    {
        // Arrange
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(
            _factory,
            _mail,
            "no-secrets@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.GetAsync(
            SessionsPath,
            TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(pair.RefreshToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(pair.AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The first `self` gate: one account never sees another's sessions.</summary>
    [Fact]
    public async Task ListSessions_DoesNotReturnAnotherAccountsSessionsAsync()
    {
        // Arrange
        (HttpClient firstClient, _) = await TestBearer.SignInAsync(_factory, _mail, "owner-a@example.com");
        (HttpClient secondClient, _) = await TestBearer.SignInAsync(_factory, _mail, "owner-b@example.com");

        using HttpClient a = firstClient;
        using HttpClient b = secondClient;

        // Act
        List<SessionRow>? theirs = await ListAsync(b);

        // Assert — one session each, and not the same one.
        List<SessionRow>? mine = await ListAsync(a);
        Assert.NotNull(mine);
        Assert.NotNull(theirs);
        Assert.Single(mine);
        Assert.Single(theirs);
        Assert.NotEqual(mine[0].SessionId, theirs[0].SessionId);
    }

    /// <summary>Another account's session answers `404`, never `403` — `docs/api/ENDPOINTS.md`.</summary>
    [Fact]
    public async Task DeleteSession_ForAnotherAccountsSession_Returns404Async()
    {
        // Arrange
        (HttpClient firstClient, _) = await TestBearer.SignInAsync(_factory, _mail, "victim@example.com");
        (HttpClient secondClient, _) = await TestBearer.SignInAsync(_factory, _mail, "attacker@example.com");

        using HttpClient victim = firstClient;
        using HttpClient attacker = secondClient;

        List<SessionRow>? victimSessions = await ListAsync(victim);
        Assert.NotNull(victimSessions);
        Guid target = victimSessions[0].SessionId;

        // Act
        using HttpResponseMessage response = await attacker.DeleteAsync(
            $"{SessionsPath}/{target}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And the victim's session is untouched.
        List<SessionRow>? afterwards = await ListAsync(victim);
        Assert.NotNull(afterwards);
        Assert.Single(afterwards);
    }

    /// <summary>An unknown session id answers `404` exactly as another account's does.</summary>
    [Fact]
    public async Task DeleteSession_ForAnUnknownSession_Returns404Async()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "unknown-session@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.DeleteAsync(
            $"{SessionsPath}/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A named session of the caller's own is ended.</summary>
    [Fact]
    public async Task DeleteSession_ForOwnSession_Returns204AndRemovesItFromTheListAsync()
    {
        // Arrange
        const string email = "ends-own@example.com";
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);
        _ = await TestBearer.SignInAgainAsync(_factory, email);

        using HttpClient authenticated = client;
        List<SessionRow>? before = await ListAsync(authenticated);
        Assert.NotNull(before);
        Assert.Equal(2, before.Count);

        // Act
        using HttpResponseMessage response = await authenticated.DeleteAsync(
            $"{SessionsPath}/{before[0].SessionId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        List<SessionRow>? after = await ListAsync(authenticated);
        Assert.NotNull(after);
        Assert.Single(after);
    }

    private static async Task<List<SessionRow>?> ListAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            SessionsPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<SessionRow>>(
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostRotationAsync(string refreshToken)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            SessionRotationEndpointTests.TokensPath,
            new { refreshToken },
            TestContext.Current.CancellationToken);
    }

    /// <summary>One row of the session list, as a client reads it.</summary>
    /// <param name="SessionId">The id the delete route takes.</param>
    /// <param name="Platform">The client platform that started the session.</param>
    /// <param name="Device">The device that started it.</param>
    /// <param name="AppVersion">The app version that started it.</param>
    internal sealed record SessionRow(Guid SessionId, string Platform, string Device, string AppVersion);
}
