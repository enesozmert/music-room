using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Validation;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Every way this provider refuses, one test each — SPEC-044.
/// </summary>
/// <remarks>
/// One test per refusal, deliberately: removing any single check in the handlers fails exactly one of these,
/// which a suite that bundled them into one "bad request" test would not do.
/// <para>
/// Two of them answer `400` without redirecting, and that is a security property rather than a style choice:
/// an unknown client and an unregistered redirect URI are the two cases where this API does not know that the
/// URI in the request belongs to anybody, so sending an error to it would make this provider an open
/// redirector with its own name on the redirect — RFC 6749 §4.1.2.1.
/// </para>
/// </remarks>
public sealed class OAuth2RefusalTests : IAsyncLifetime
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

    /// <summary>A code is spent once. The second attempt is refused by the database, not by a read.</summary>
    [Fact]
    public async Task ASpentCode_IsRefusedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-replay@example.com");

        string verifier = OAuth2Driver.Verifier();
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: null);

        using HttpResponseMessage first = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);
        using HttpResponseMessage second = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await OAuth2Driver.AssertErrorAsync(second, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>A wrong verifier is refused — the whole point of PKCE.</summary>
    /// <remarks>
    /// It also burns the code, which is deliberate: the exchange is arbitrated by the database before the
    /// comparison, so there is never a live code to guess a verifier against. The second assertion is that
    /// the right verifier does not rescue it afterwards.
    /// </remarks>
    [Fact]
    public async Task AWrongVerifier_IsRefusedAndBurnsTheCodeAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-pkce@example.com");

        string verifier = OAuth2Driver.Verifier();
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: null);

        using HttpResponseMessage wrong = await OAuth2Driver.ExchangeAsync(
            _factory, code, OAuth2Driver.Verifier());
        using HttpResponseMessage right = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);

        await OAuth2Driver.AssertErrorAsync(wrong, HttpStatusCode.BadRequest, "invalid_grant");
        await OAuth2Driver.AssertErrorAsync(right, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>A code presented against another redirect URI is refused — RFC 6749 §4.1.3.</summary>
    /// <remarks>
    /// Without the comparison, a code intercepted on its way to one URI could be redeemed by whoever
    /// controls another.
    /// </remarks>
    [Fact]
    public async Task ACodeRedeemedAgainstAnotherUri_IsRefusedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-uri@example.com");

        string verifier = OAuth2Driver.Verifier();
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: null);

        using HttpResponseMessage exchanged = await OAuth2Driver.ExchangeAsync(
            _factory, code, verifier, OAuth2Driver.UnregisteredUri);

        await OAuth2Driver.AssertErrorAsync(exchanged, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>An expired code is refused. Aged in the database, because 60 seconds is not a test's wait.</summary>
    [Fact]
    public async Task AnExpiredCode_IsRefusedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-expiry@example.com");

        string verifier = OAuth2Driver.Verifier();
        string code = await OAuth2Driver.AuthorizeAsync(signedIn, verifier, nonce: null);

        await OAuth2Driver.AgeAsync(_factory, code);

        using HttpResponseMessage exchanged = await OAuth2Driver.ExchangeAsync(_factory, code, verifier);

        await OAuth2Driver.AssertErrorAsync(exchanged, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>A code nobody issued is refused, and says no more than a spent one does.</summary>
    [Fact]
    public async Task AnUnknownCode_IsRefusedAsync()
    {
        using HttpResponseMessage exchanged = await OAuth2Driver.ExchangeAsync(
            _factory, OAuth2Driver.Verifier(), OAuth2Driver.Verifier());

        await OAuth2Driver.AssertErrorAsync(exchanged, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary><c>code_challenge_method=plain</c> is refused at the authorization endpoint.</summary>
    /// <remarks>
    /// Refused by redirect rather than by a `400`, because the client and its URI are both registered: an
    /// error belongs at the address the client is waiting on, and `state` goes back with it.
    /// </remarks>
    [Fact]
    public async Task APlainChallenge_IsRefusedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-plain@example.com");

        using HttpResponseMessage response = await signedIn.GetAsync(
            OAuth2Driver.Authorize(
                PkceChallenge.Of(OAuth2Driver.Verifier()), method: "plain", nonce: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        Dictionary<string, StringValues> returned = OAuth2Driver.Returned(response);

        Assert.Equal("invalid_request", returned["error"].ToString());
        Assert.Equal(OAuth2Driver.State, returned["state"].ToString());
    }

    /// <summary>A response type this provider does not offer is refused by name.</summary>
    [Fact]
    public async Task AnUnsupportedResponseType_IsRefusedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-response@example.com");

        string request = OAuth2Driver
            .Authorize(PkceChallenge.Of(OAuth2Driver.Verifier()), PkceChallenge.Method, null)
            .Replace("response_type=code", "response_type=token", StringComparison.Ordinal);

        using HttpResponseMessage response = await signedIn.GetAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            "unsupported_response_type",
            OAuth2Driver.Returned(response)["error"].ToString());
    }

    /// <summary>An unregistered redirect URI answers `400` and does not redirect.</summary>
    [Fact]
    public async Task AnUnregisteredRedirectUri_IsNotRedirectedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-open@example.com");

        using HttpResponseMessage response = await signedIn.GetAsync(
            OAuth2Driver.Authorize(
                PkceChallenge.Of(OAuth2Driver.Verifier()),
                PkceChallenge.Method,
                null,
                redirectUri: OAuth2Driver.UnregisteredUri),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await OAuth2Driver.AssertCodeAsync(response, ValidationCodes.RedirectUriUnregistered);
    }

    /// <summary>A client this deployment does not register answers `400`, and never redirects either.</summary>
    [Fact]
    public async Task AnUnknownClient_IsNotRedirectedAsync()
    {
        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(
            _factory, _mail, "oauth-client@example.com");

        using HttpResponseMessage response = await signedIn.GetAsync(
            OAuth2Driver.Authorize(
                PkceChallenge.Of(OAuth2Driver.Verifier()),
                PkceChallenge.Method,
                null,
                clientId: "somebody-else"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await OAuth2Driver.AssertCodeAsync(response, ValidationCodes.ClientUnknown);
    }

    /// <summary>Without a bearer token there is no authorization request — the owner's answer A.</summary>
    [Fact]
    public async Task AnAnonymousAuthorizeRequest_Is401Async()
    {
        using HttpClient anonymous = OAuth2Driver.Redirectless(_factory);

        using HttpResponseMessage response = await anonymous.GetAsync(
            OAuth2Driver.Authorize(
                PkceChallenge.Of(OAuth2Driver.Verifier()), PkceChallenge.Method, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A grant OAuth 2.1 removed is refused by name — ADR-0031.</summary>
    [Fact]
    public async Task ThePasswordGrant_IsUnsupportedAsync()
    {
        using HttpResponseMessage response = await OAuth2Driver.PostAsync(
            _factory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["username"] = "oauth-password@example.com",
                ["password"] = LoginEndpointTests.AccountPassword,
            });

        await OAuth2Driver.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "unsupported_grant_type");
    }
}
