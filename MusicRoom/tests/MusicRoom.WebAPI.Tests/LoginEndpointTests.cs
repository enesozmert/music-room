using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Middleware;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Signing in end to end — testing standard. A real PostgreSQL in a container, because the credential
/// read and the refresh-token insert are what the endpoint is made of (ADR-0012).
/// </summary>
public sealed class LoginEndpointTests : IAsyncLifetime
{
    internal const string SessionsPath = "/api/v1/auth/sessions";
    internal const string RegistrationPath = "/api/v1/auth/registrations";
    internal const string VerificationsPath = "/api/v1/auth/email-verifications";
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

    /// <summary>The happy path: a verified account exchanges its password for a pair — subject V.1.</summary>
    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsTokenPairAsync()
    {
        // Arrange
        const string email = "signs-in@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync(email, AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenPair? pair = await response.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(pair);
        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.True(pair.ExpiresIn > 0);

        // The plaintext was returned to the caller and nothing else: the row keeps a digest.
        Assert.Equal(1, await CountRefreshTokensAsync(email));
        Assert.False(await AnyRefreshTokenHashEqualsAsync(pair.RefreshToken));
    }

    /// <summary>The password is actually verified — subject V.6.</summary>
    [Fact]
    public async Task Login_WithWrongPassword_Returns401Async()
    {
        // Arrange
        const string email = "wrong-password@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync(email, "not the password at all");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountRefreshTokensAsync(email));
    }

    /// <summary>
    /// An address nobody registered answers exactly as a wrong password does — subject V.6. Same
    /// status, same empty body: the response is not an enumeration oracle.
    /// </summary>
    [Fact]
    public async Task Login_WithUnknownAddress_ReturnsTheSameBodyAndStatusAsAWrongPasswordAsync()
    {
        // Arrange
        const string email = "compared@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage unknown = await PostLoginAsync("nobody@example.com", AccountPassword);
        using HttpResponseMessage wrongPassword = await PostLoginAsync(email, "not the password at all");

        // Assert
        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An unverified account cannot use the services — SDD §11.1.</summary>
    [Fact]
    public async Task Login_WithUnverifiedAccount_Returns401Async()
    {
        // Arrange — registered, mail issued, link never clicked.
        const string email = "unverified@example.com";
        _ = await RegisterAndCaptureTokenAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync(email, AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await CountRefreshTokensAsync(email));
    }

    /// <summary>The address is matched case-insensitively, the way registration stores it.</summary>
    [Fact]
    public async Task Login_WithADifferentlyCasedAddress_ReturnsTokenPairAsync()
    {
        // Arrange
        const string email = "mixed-case@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync("Mixed-Case@Example.com", AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task RegisterAndVerifyAsync(string email)
    {
        string token = await RegisterAndCaptureTokenAsync(email);

        using HttpClient client = _factory.CreateAuditedClient();
        using HttpResponseMessage verified = await client.PostAsJsonAsync(
            VerificationsPath,
            new { token },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    private async Task<string> RegisterAndCaptureTokenAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpClient client = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationPath,
            new { email, password = AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, _mail.Tokens.Count);

        return _mail.Tokens[^1];
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string password)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            SessionsPath,
            new { email, password },
            TestContext.Current.CancellationToken);
    }

    private async Task<int> CountRefreshTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .CountAsync(
                token => context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task<bool> AnyRefreshTokenHashEqualsAsync(string plaintext)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .AnyAsync(
                token => token.TokenHash == plaintext,
                TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// The JWT settings a test host needs, because SPEC-005 made them start-up requirements with no
/// default.
/// </summary>
/// <remarks>
/// The key is a test value and nothing signs with it outside this assembly; it exists so the host can
/// start, in the same way the Testcontainers connection string exists so it can connect.
/// </remarks>
internal static class TestJwtSettings
{
    /// <summary>Applies the four settings to a test host builder.</summary>
    /// <param name="webHost">The builder being configured.</param>
    internal static void Apply(IWebHostBuilder webHost)
    {
        webHost.UseSetting("JWT_SIGNING_KEY", "musicroom-integration-test-signing-key-32");
        webHost.UseSetting("JWT_ISSUER", "musicroom-tests");
        webHost.UseSetting("JWT_AUDIENCE", "musicroom-tests-client");
        webHost.UseSetting("JWT_ACCESS_TOKEN_MINUTES", "15");
    }
}

/// <summary>
/// A client that carries the mandatory client headers on every request — SDD §10.7, enforced by SPEC-008.
/// </summary>
/// <remarks>
/// Every mutating call in every suite needs them, so the helper is here rather than repeated. A suite that
/// is proving the header rule itself builds its own client instead.
/// </remarks>
internal static class TestClientContext
{
    /// <summary>The platform value the suites send.</summary>
    internal const string Platform = "test-harness";

    /// <summary>The device value the suites send.</summary>
    internal const string Device = "ci-runner";

    /// <summary>The app version the suites send.</summary>
    internal const string AppVersion = "0.0.0-test";

    /// <summary>Creates a client whose default headers satisfy the middleware.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="appVersion">
    /// The app version to report. Overridden only by SPEC-020's test that a phone which updates its app is still
    /// the same device.
    /// </param>
    /// <returns>A client ready to send a mutating request.</returns>
    internal static HttpClient CreateAuditedClient(
        this WebApplicationFactory<Program> factory,
        string? appVersion = null)
    {
        HttpClient client = factory.CreateClient();

        client.DefaultRequestHeaders.Add(ClientContextMiddleware.PlatformHeader, Platform);
        client.DefaultRequestHeaders.Add(ClientContextMiddleware.DeviceHeader, Device);
        client.DefaultRequestHeaders.Add(ClientContextMiddleware.AppVersionHeader, appVersion ?? AppVersion);

        return client;
    }
}

/// <summary>
/// The rate limits a test host uses when limiting is not what the suite is proving.
/// </summary>
/// <remarks>
/// SPEC-006 attached a per-IP policy to every credential endpoint and a per-account limit inside the
/// handlers. A suite that signs in six times, or fires twenty parallel requests to prove a race, trips
/// both — so these suites raise the permits instead of skipping a test or weakening an assertion. The
/// limits themselves are proven by `CredentialRateLimitingTests` and `CredentialAttemptLimiterTests`.
/// </remarks>
internal static class TestRateLimitSettings
{
    /// <summary>High enough that no suite reaches it, low enough to stay a number.</summary>
    internal const string Permits = "100000";

    /// <summary>Raises every credential limit on a test host.</summary>
    /// <param name="webHost">The builder being configured.</param>
    internal static void Relax(IWebHostBuilder webHost)
    {
        foreach (string prefix in new[]
        {
            "RATE_LIMIT_SESSIONS",
            "RATE_LIMIT_TOKENS",
            "RATE_LIMIT_REGISTRATIONS",
            "RATE_LIMIT_VERIFICATION_REQUESTS",
            "RATE_LIMIT_EMAIL_VERIFICATIONS",
            "RATE_LIMIT_PASSWORD_RESET_REQUESTS",
            "RATE_LIMIT_PASSWORD_RESETS",
        })
        {
            webHost.UseSetting($"{prefix}_PERMITS", Permits);
        }

        webHost.UseSetting("RATE_LIMIT_ACCOUNT_PERMITS", Permits);
    }
}

/// <summary>
/// Builds a client that carries a bearer token — SPEC-010.
/// </summary>
/// <remarks>
/// The token comes from a real sign-in rather than being forged: a suite that minted its own would pass with a
/// broken issuer, and the point of these endpoints is that the two halves agree.
/// </remarks>
internal static class TestBearer
{
    /// <summary>Registers, verifies, signs in, and returns a client that presents the access token.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="mail">The recording sender, which is the only way to read the verification token.</param>
    /// <param name="email">The address to create.</param>
    /// <returns>An authenticated client and the pair it was signed in with.</returns>
    internal static async Task<(HttpClient Client, TokenPair Pair)> SignInAsync(
        WebApplicationFactory<Program> factory,
        RecordingMailService mail,
        string email)
    {
        using HttpClient anonymous = factory.CreateAuditedClient();

        int before = mail.Tokens.Count;

        using HttpResponseMessage registered = await anonymous.PostAsJsonAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(before + 1, mail.Tokens.Count);

        using HttpResponseMessage verified = await anonymous.PostAsJsonAsync(
            LoginEndpointTests.VerificationsPath,
            new { token = mail.Tokens[^1] },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        TokenPair pair = await PairAsync(anonymous, email);

        HttpClient authenticated = factory.CreateAuditedClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.AccessToken);

        return (authenticated, pair);
    }

    /// <summary>Signs an existing account in again, which starts a second session.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="email">The address to sign in.</param>
    /// <returns>The new pair.</returns>
    internal static async Task<TokenPair> SignInAgainAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        using HttpClient anonymous = factory.CreateAuditedClient();

        return await PairAsync(anonymous, email);
    }

    private static async Task<TokenPair> PairAsync(HttpClient client, string email)
    {
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
}

/// <summary>
/// The token pair the session endpoints answer with, as a client reads it.
/// </summary>
/// <param name="AccessToken">The signed JWT.</param>
/// <param name="RefreshToken">The opaque refresh token, in plaintext.</param>
/// <param name="ExpiresIn">The access token's lifetime in seconds.</param>
internal sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);
