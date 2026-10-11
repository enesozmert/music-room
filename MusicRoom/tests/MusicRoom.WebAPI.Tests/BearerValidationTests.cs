using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Infrastructure.Security;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What the bearer handler refuses — SPEC-010, RFC 9700.
/// </summary>
/// <remarks>
/// Each token here is forged deliberately, one property wrong at a time, because "the endpoint accepted a
/// token" is only interesting next to the tokens it must not accept.
/// <para>
/// The forged tokens are **RS256 signed with the host's own key** since SPEC-050, read out of the test
/// host's container. One of them is deliberately HMAC, because "no HMAC overlap" is a decision and a
/// decision with no test is a comment.
/// </para>
/// </remarks>
public sealed class BearerValidationTests : IAsyncLifetime
{
    private static readonly JsonWebTokenHandler _handler = new();

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine and brings the schema up.</summary>
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

    /// <summary>No token at all: the gate exists.</summary>
    [Fact]
    public async Task Sessions_WithoutABearerToken_Returns401Async()
    {
        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token: null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Garbage is refused, not mistaken for a server fault.</summary>
    [Fact]
    public async Task Sessions_WithAMalformedToken_Returns401Async()
    {
        // Act
        using HttpResponseMessage response = await GetSessionsAsync("this.is.not.a.jwt");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A token signed by another key is not ours, however well-formed.</summary>
    [Fact]
    public async Task Sessions_WithATokenSignedByAnotherKey_Returns401Async()
    {
        // Arrange — a well-formed RS256 token from a key this host never published.
        using RSA stranger = RSA.Create(2048);
        string token = Forge(key: new RsaSecurityKey(stranger) { KeyId = "a-stranger" });

        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token signed with an HMAC secret is refused — the overlap SPEC-050 deliberately did not keep.
    /// </summary>
    /// <remarks>
    /// Until SPEC-050 this was the shape of every access token. Keeping it accepted "for a window" would
    /// have kept a shared secret that still mints valid tokens, plus a knob to remember to turn off; the
    /// cost of not keeping it is one `401` per client at deploy, and the refresh token is untouched.
    /// </remarks>
    [Fact]
    public async Task Sessions_WithAnHmacSignedToken_Returns401Async()
    {
        // Arrange
        string token = Forge(
            key: new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("musicroom-integration-test-signing-key-32")));

        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>`aud` is checked, which RFC 9700 asks for explicitly.</summary>
    [Fact]
    public async Task Sessions_WithAWrongAudienceToken_Returns401Async()
    {
        // Arrange
        string token = Forge(audience: "somebody-elses-api");

        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>`iss` is checked too.</summary>
    [Fact]
    public async Task Sessions_WithAWrongIssuerToken_Returns401Async()
    {
        // Arrange
        string token = Forge(issuer: "not-this-service");

        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An expired token is refused, and the 30-second skew the owner chose is not a loophole: this one is an
    /// hour old — SPEC-010 escalation 3.
    /// </summary>
    [Fact]
    public async Task Sessions_WithAnExpiredToken_Returns401Async()
    {
        // Arrange
        string token = Forge(issuedAt: DateTime.UtcNow.AddHours(-1), lifetime: TimeSpan.FromMinutes(15));

        // Act
        using HttpResponseMessage response = await GetSessionsAsync(token);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A token this application issued is accepted, which is what makes the refusals meaningful.</summary>
    [Fact]
    public async Task Sessions_WithAnIssuedToken_Returns200Async()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "accepted@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.GetAsync(
            SessionEndpointTests.SessionsPath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetSessionsAsync(string? token)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.GetAsync(SessionEndpointTests.SessionsPath, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Mints a token that is correct except for the property under test. The defaults match what the test host
    /// is configured with, so exactly one thing is ever wrong.
    /// </summary>
    private string Forge(
        SecurityKey? key = null,
        string issuer = "musicroom-tests",
        string audience = "musicroom-tests-client",
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null)
    {
        DateTime nowUtc = issuedAt ?? DateTime.UtcNow;

        // The host's own key by default, so exactly one property is ever wrong. Read from the container
        // rather than from a constant: since SPEC-050 the signature is asymmetric and the private half is
        // generated per test process, so there is no constant to share.
        SecurityKey signingKey = key ?? _factory.Services.GetRequiredService<ISigningKeyProvider>().Current.Key;

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = nowUtc + (lifetime ?? TimeSpan.FromMinutes(15)),
            SigningCredentials = new SigningCredentials(
                signingKey,
                signingKey is SymmetricSecurityKey
                    ? SecurityAlgorithms.HmacSha256
                    : SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["sid"] = Guid.NewGuid().ToString(),
                ["jti"] = Guid.NewGuid().ToString(),
            },
        };

        return _handler.CreateToken(descriptor);
    }
}
