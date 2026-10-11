using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure.Identity;
using MusicRoom.Persistence.Contexts;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What a client does to this provider — the driver <see cref="OAuth2FlowTests"/> and
/// <see cref="OAuth2RefusalTests"/> share, SPEC-044.
/// </summary>
/// <remarks>
/// Its own file so each suite stays inside the 300-line limit the root .editorconfig enforces, and so there
/// is one definition of "how a client drives this flow": two copies would let the happy path and the
/// refusals drift apart, which is exactly where a flow stops being tested.
/// </remarks>
internal static class OAuth2Driver
{
    /// <summary>The one client a test host registers.</summary>
    internal const string ClientId = "musicroom-app";

    /// <summary>Its one registered redirect URI.</summary>
    internal const string RedirectUri = "https://app.musicroom.test/callback";

    /// <summary>A URI nobody registered, for the refusals that must not redirect.</summary>
    internal const string UnregisteredUri = "https://not-registered.test/callback";

    /// <summary>Where the token endpoint lives.</summary>
    internal const string TokenPath = "/oauth2/token";

    /// <summary>Where the authorization endpoint lives.</summary>
    internal const string AuthorizePath = "/oauth2/authorize";

    /// <summary>Where UserInfo lives — SPEC-045.</summary>
    internal const string UserInfoPath = "/oauth2/userinfo";

    /// <summary>Where revocation lives — SPEC-045.</summary>
    internal const string RevocationPath = "/oauth2/revoke";

    /// <summary>The value a client sends to recognise its own request.</summary>
    internal const string State = "the-clients-own-state";

    /// <summary>Registers one client on a test host — SPEC-044 decision 1, a row of configuration.</summary>
    /// <param name="webHost">The builder being configured.</param>
    internal static void Apply(IWebHostBuilder webHost)
    {
        webHost.UseSetting(ConfiguredOpenIdClientRegistry.ClientIdVariable, ClientId);
        webHost.UseSetting(ConfiguredOpenIdClientRegistry.RedirectUrisVariable, RedirectUri);
    }

    /// <summary>
    /// Signs an account in and returns a client that presents its token and does not follow redirects.
    /// </summary>
    /// <param name="factory">The test host.</param>
    /// <param name="mail">The recording sender, which is how the verification token is read.</param>
    /// <param name="email">The address to create.</param>
    /// <returns>The authenticated client.</returns>
    /// <remarks>
    /// Redirects are the answer under test, so following them would consume what is being asserted — and the
    /// registered URI is another host, which a test server cannot answer anyway.
    /// </remarks>
    internal static async Task<HttpClient> SignedInAsync(
        WebApplicationFactory<Program> factory,
        RecordingMailService mail,
        string email)
    {
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(factory, mail, email);
        issued.Dispose();

        HttpClient signedIn = Redirectless(factory);
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.AccessToken);

        return signedIn;
    }

    /// <summary>A client that reports a redirect instead of following it.</summary>
    /// <param name="factory">The test host.</param>
    /// <returns>The client.</returns>
    internal static HttpClient Redirectless(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Drives the whole flow and returns the pair it ends with, plus a client holding the access token.
    /// </summary>
    /// <param name="factory">The test host.</param>
    /// <param name="mail">The recording sender.</param>
    /// <param name="email">The address to create.</param>
    /// <param name="scope">The scopes to ask for, which is what UserInfo then filters on.</param>
    /// <returns>A client presenting the granted access token, and the raw token response.</returns>
    internal static async Task<(HttpClient Client, JsonDocument Body)> GrantedAsync(
        WebApplicationFactory<Program> factory,
        RecordingMailService mail,
        string email,
        string scope)
    {
        using HttpClient signedIn = await SignedInAsync(factory, mail, email);

        string verifier = OAuth2Checks.Verifier();

        using HttpResponseMessage redirected = await signedIn.GetAsync(
            Authorize(PkceChallenge.Of(verifier), PkceChallenge.Method, null, new AuthorizeOverrides(Scope: scope)),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, redirected.StatusCode);

        using HttpResponseMessage exchanged = await ExchangeAsync(
            factory, OAuth2Checks.Returned(redirected)["code"].ToString(), verifier);

        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);

        JsonDocument body = JsonDocument.Parse(
            await exchanged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        HttpClient granted = factory.CreateAuditedClient();
        granted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.RootElement.GetProperty("access_token").GetString());

        return (granted, body);
    }

    /// <summary>Reads UserInfo with a client that holds an access token.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> UserInfoAsync(HttpClient client) =>
        client.GetAsync(new Uri(UserInfoPath, UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>Posts a revocation.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="token">The token to revoke.</param>
    /// <param name="hint">A `token_type_hint`, or null to send none.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> RevokeAsync(
        WebApplicationFactory<Program> factory,
        string token,
        string? hint = null)
    {
        Dictionary<string, string> form = new(StringComparer.Ordinal) { ["token"] = token };

        if (hint is not null)
        {
            form["token_type_hint"] = hint;
        }

        return PostFormAsync(factory, RevocationPath, form);
    }

    /// <summary>The authorization request a conforming client sends.</summary>
    /// <param name="challenge">The PKCE challenge.</param>
    /// <param name="method">The challenge method. `S256`, or `plain` to prove it is refused.</param>
    /// <param name="nonce">The nonce, or null to prove none is invented.</param>
    /// <param name="overrides">What to send instead of the defaults, or null for all of them.</param>
    /// <returns>The relative URL, query string included.</returns>
    internal static string Authorize(
        string challenge,
        string method,
        string? nonce,
        AuthorizeOverrides? overrides = null)
    {
        AuthorizeOverrides sent = overrides ?? new AuthorizeOverrides();

        Dictionary<string, string?> parameters = new(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = sent.ClientId,
            ["redirect_uri"] = sent.RedirectUri,
            ["scope"] = sent.Scope,
            ["state"] = State,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = method,
        };

        if (nonce is not null)
        {
            parameters["nonce"] = nonce;
        }

        return QueryHelpers.AddQueryString(AuthorizePath, parameters);
    }

    /// <summary>Drives the authorization endpoint and returns the code it redirected with.</summary>
    /// <param name="signedIn">A client holding an access token.</param>
    /// <param name="verifier">The PKCE verifier the challenge is derived from.</param>
    /// <param name="nonce">The nonce to send, or null.</param>
    /// <returns>The authorization code.</returns>
    internal static async Task<string> AuthorizeAsync(HttpClient signedIn, string verifier, string? nonce)
    {
        using HttpResponseMessage response = await signedIn.GetAsync(
            Authorize(PkceChallenge.Of(verifier), PkceChallenge.Method, nonce),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        Dictionary<string, StringValues> returned = OAuth2Checks.Returned(response);

        Assert.Equal(State, returned["state"].ToString());

        return returned["code"].ToString();
    }

    /// <summary>Exchanges a code at the token endpoint.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="code">The code.</param>
    /// <param name="verifier">The verifier.</param>
    /// <param name="redirectUri">The URI to claim the code was issued against.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> ExchangeAsync(
        WebApplicationFactory<Program> factory,
        string code,
        string verifier,
        string redirectUri = RedirectUri) =>
        PostAsync(factory, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        });

    /// <summary>Posts a form to the token endpoint, with the mandatory client headers.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="form">The grant parameters.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> PostAsync(
        WebApplicationFactory<Program> factory,
        Dictionary<string, string> form) =>
        PostFormAsync(factory, TokenPath, form);

    /// <summary>Posts a form to one of the provider's routes, with the mandatory client headers.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="path">The route.</param>
    /// <param name="form">The parameters.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> PostFormAsync(
        WebApplicationFactory<Program> factory,
        string path,
        Dictionary<string, string> form)
    {
        using HttpClient client = factory.CreateAuditedClient();

        return await client.PostAsync(
            new Uri(path, UriKind.Relative),
            new FormUrlEncodedContent(form),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Validates an ID token with the published key set and the issuer, and nothing else.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="idToken">The token to validate.</param>
    /// <returns>The validation result.</returns>
    internal static async Task<TokenValidationResult> ValidateIdTokenAsync(
        WebApplicationFactory<Program> factory,
        string idToken)
    {
        using HttpClient client = factory.CreateClient();

        string keys = await client.GetStringAsync(
            new Uri("/.well-known/jwks.json", UriKind.Relative), TestContext.Current.CancellationToken);

        return await new JsonWebTokenHandler().ValidateTokenAsync(
            idToken,
            new TokenValidationParameters
            {
                ValidIssuer = client.BaseAddress!.ToString().TrimEnd('/'),
                ValidAudience = ClientId,
                IssuerSigningKeys = new JsonWebKeySet(keys).GetSigningKeys(),
                ValidateLifetime = true,
            });
    }

    /// <summary>Moves a code's expiry into the past, which is the only honest way to test a 60-second one.</summary>
    /// <param name="factory">The test host.</param>
    /// <param name="code">The code to age.</param>
    /// <returns>A task that completes when the row is aged.</returns>
    internal static async Task AgeAsync(WebApplicationFactory<Program> factory, string code)
    {
        string digest = OpaqueTokenHash.Of(code);

        using IServiceScope scope = factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        await context.Set<AuthorizationCode>()
            .Where(candidate => candidate.CodeHash == digest)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.ExpiresAtUtc,
                    DateTime.UtcNow.AddSeconds(-1)),
                TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// What a test sends instead of the defaults — one record, because six parameters is one over the limit.
/// </summary>
/// <param name="ClientId">The client, overridable to prove an unknown one is refused.</param>
/// <param name="RedirectUri">The URI, overridable to prove an unregistered one is refused.</param>
/// <param name="Scope">What to ask for, which is what UserInfo then filters on.</param>
internal sealed record AuthorizeOverrides(
    string ClientId = OAuth2Driver.ClientId,
    string RedirectUri = OAuth2Driver.RedirectUri,
    string Scope = "openid");
