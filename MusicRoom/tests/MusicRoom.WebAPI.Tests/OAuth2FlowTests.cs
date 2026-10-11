using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The authorization code flow, end to end — ADR-0031, SPEC-044.
/// </summary>
/// <remarks>
/// What this suite proves is that the two halves fit: a code from the authorization endpoint becomes a
/// session and an ID token at the token endpoint, and the ID token verifies against the key set the
/// discovery document publishes — checked the way a client checks it, with the JWKS and the issuer and
/// nothing from inside the process.
/// <para>
/// The account signs in through the password endpoints first, because under the owner's answer A the
/// authorization endpoint requires a bearer token: this is a first-party provider, and this suite is what
/// that sentence looks like in a request. The refusals are <see cref="OAuth2RefusalTests"/>.
/// </para>
/// </remarks>
public sealed class OAuth2FlowTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, registers one client, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            OAuth2Driver.Apply(webHost);
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

    /// <summary>The whole flow: a code, then a session and an ID token a stranger can verify.</summary>
    [Fact]
    public async Task TheFlow_IssuesASessionAndAnIdTokenAsync()
    {
        // Arrange
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-flow@example.com");

        string verifier = OAuth2Driver.Verifier();

        // Act
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: "client-chose-this");

        using HttpResponseMessage exchanged = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);

        // Assert
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await exchanged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.NotEmpty(body.RootElement.GetProperty("access_token").GetString()!);
        Assert.NotEmpty(body.RootElement.GetProperty("refresh_token").GetString()!);
        Assert.Equal("openid", body.RootElement.GetProperty("scope").GetString());

        TokenValidationResult validated = await OAuth2Driver.ValidateIdTokenAsync(
            _factory,
            body.RootElement.GetProperty("id_token").GetString()!);

        Assert.True(validated.IsValid, validated.Exception?.Message);

        // Echoed, not invented: the client sent this to bind the token to its own request.
        Assert.Equal("client-chose-this", validated.ClaimsIdentity.FindFirst("nonce")!.Value);
    }

    /// <summary>The access token the exchange answers with opens the rest of the API.</summary>
    /// <remarks>
    /// Asserted because it is the claim that makes this flow a replacement for `POST /auth/sessions` rather
    /// than a parallel one: the same bearer token, validated by the same middleware.
    /// </remarks>
    [Fact]
    public async Task TheAccessToken_OpensTheRestOfTheApiAsync()
    {
        // Arrange
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-access@example.com");

        string verifier = OAuth2Driver.Verifier();
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: null);

        using HttpResponseMessage exchanged = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await exchanged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using HttpClient granted = _factory.CreateAuditedClient();
        granted.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            body.RootElement.GetProperty("access_token").GetString());

        // Act
        using HttpResponseMessage sessions = await granted.GetAsync(
            new Uri(SessionEndpointTests.SessionsPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
    }

    /// <summary>The refresh grant rotates the session, and asserts no new authentication.</summary>
    /// <remarks>
    /// No `id_token`: nobody proved anything to this API, a stored token was presented. OpenID Connect Core
    /// 1.0 §12.1 leaves it optional, and this is what leaving it out looks like.
    /// </remarks>
    [Fact]
    public async Task TheRefreshGrant_RotatesAndAssertsNoAuthenticationAsync()
    {
        // Arrange
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "oauth-refresh@example.com");
        issued.Dispose();

        // Act
        using HttpResponseMessage rotated = await OAuth2Driver.PostAsync(
            _factory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = pair.RefreshToken,
            });

        // Assert
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await rotated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.NotEqual(pair.RefreshToken, body.RootElement.GetProperty("refresh_token").GetString());
        Assert.False(body.RootElement.TryGetProperty("id_token", out _));
    }

    /// <summary>A reused refresh token revokes the family, through S1's own rotation — ADR-0032.</summary>
    /// <remarks>
    /// The reason the refresh grant reuses <c>RotateSessionCommand</c> rather than growing a second
    /// mechanism: the theft detection is not reimplemented here, so it cannot be left out here.
    /// </remarks>
    [Fact]
    public async Task AReusedRefreshToken_RevokesTheFamilyAsync()
    {
        // Arrange
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "oauth-reuse@example.com");
        issued.Dispose();

        using HttpResponseMessage first = await RefreshAsync(pair.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string rotated = body.RootElement.GetProperty("refresh_token").GetString()!;

        // Act — the spent token again, which is what a theft looks like from here.
        using HttpResponseMessage replayed = await RefreshAsync(pair.RefreshToken);

        // Assert
        await OAuth2Driver.AssertErrorAsync(replayed, HttpStatusCode.BadRequest, "invalid_grant");

        // And the family it belonged to is gone with it, so the rotated token is worthless too.
        using HttpResponseMessage afterRevocation = await RefreshAsync(rotated);

        await OAuth2Driver.AssertErrorAsync(afterRevocation, HttpStatusCode.BadRequest, "invalid_grant");
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        OAuth2Driver.PostAsync(
            _factory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });
}
