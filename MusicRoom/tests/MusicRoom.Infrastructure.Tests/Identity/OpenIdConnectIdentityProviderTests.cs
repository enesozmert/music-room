using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Identity;
using MusicRoom.Infrastructure.Messaging;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Identity;

/// <summary>
/// The relying-party core, check by check — ADR-0008, SPEC-041.
/// </summary>
/// <remarks>
/// **Each check is refused on its own.** A suite that only sent one bad token would prove that something
/// refuses it, not that five different things each refuse what they are there for: remove the audience check
/// and a test asserting "a forged token is refused" still passes because the signature check caught it. So
/// every token below is valid in every respect except the one the test is about.
/// <para>
/// The provider is a transport that answers from memory and keys this suite controls. A test against Google
/// could not forge a wrong issuer, which is the one thing worth asserting here.
/// </para>
/// </remarks>
public sealed class OpenIdConnectIdentityProviderTests
{
    private const string _issuer = "https://provider.example.com";
    private const string _clientId = "client-id-under-test";
    private const string _subject = "1234567890";
    private const string _discoveryPath = "https://provider.example.com/.well-known/openid-configuration";

    private static readonly RsaSecurityKey _signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };
    private static readonly RsaSecurityKey _otherKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    /// <summary>A valid token yields the identity, and only then.</summary>
    [Fact]
    public async Task AValidToken_YieldsTheIdentityAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token());

        Assert.Equal(SocialAuthenticationOutcome.Authenticated, result.Outcome);
        Assert.NotNull(result.Identity);
        Assert.Equal(_subject, result.Identity.ProviderUserId);
        Assert.Equal("person@example.com", result.Identity.Email);
        Assert.True(result.Identity.IsEmailVerified);
    }

    /// <summary>A signature from another key is refused — check one, alone.</summary>
    [Fact]
    public async Task ATokenSignedByAnotherKey_IsRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token(new Forgery(Key: _otherKey)));

        Assert.Equal(SocialAuthenticationOutcome.TokenInvalid, result.Outcome);
    }

    /// <summary>A wrong issuer is refused — check two, with a valid signature.</summary>
    /// <remarks>
    /// Signed by the key this provider publishes, so nothing but the `iss` comparison can refuse it. That is
    /// what makes this a test of the issuer check rather than of the signature check.
    /// </remarks>
    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token(new Forgery(Issuer: "https://attacker.example.com")));

        Assert.Equal(SocialAuthenticationOutcome.TokenInvalid, result.Outcome);
    }

    /// <summary>A token minted for another application is refused — check three, token substitution.</summary>
    [Fact]
    public async Task ATokenForAnotherAudience_IsRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token(new Forgery(Audience: "somebody-elses-client-id")));

        Assert.Equal(SocialAuthenticationOutcome.TokenInvalid, result.Outcome);
    }

    /// <summary>An expired token is refused — check four, beyond the clock skew.</summary>
    [Fact]
    public async Task AnExpiredToken_IsRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(
            Token(new Forgery(Expires: DateTime.UtcNow.AddMinutes(-10))));

        Assert.Equal(SocialAuthenticationOutcome.TokenInvalid, result.Outcome);
    }

    /// <summary>A token with no subject is refused: there is nothing to key an identity on.</summary>
    [Fact]
    public async Task ATokenWithNoSubject_IsRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token(new Forgery(Subject: null)));

        Assert.Equal(SocialAuthenticationOutcome.TokenInvalid, result.Outcome);
    }

    /// <summary>
    /// An unverified address is reported rather than refused here — check five, and where it is enforced.
    /// </summary>
    /// <remarks>
    /// The core says what the provider asserted; the handler decides what to do about it. Refusing in both
    /// places would mean a linked identity whose address a provider later un-verified could never sign in
    /// again, which is a different decision from the one ADR-0008 took.
    /// </remarks>
    [Fact]
    public async Task AnUnverifiedAddress_IsReportedNotRefusedAsync()
    {
        SocialAuthentication result = await AuthenticateAsync(Token(new Forgery(EmailVerified: false)));

        Assert.Equal(SocialAuthenticationOutcome.Authenticated, result.Outcome);
        Assert.False(result.Identity!.IsEmailVerified);
    }

    /// <summary>A provider nobody configured is refused before any network call.</summary>
    [Fact]
    public async Task AnUnknownProvider_IsRefusedWithoutCallingAnythingAsync()
    {
        ProviderTransport transport = new(Discovery(), Keys(), TokenBody(Token()));
        ISocialIdentityProvider core = Core(transport);

        SocialAuthentication result = await core.AuthenticateAsync(
            new SocialAuthenticationRequest("nobody", "code", "verifier", "https://app.example.com/callback"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SocialAuthenticationOutcome.UnknownProvider, result.Outcome);
        Assert.Empty(transport.Requests);
    }

    /// <summary>A provider that refuses the code never reaches validation.</summary>
    [Fact]
    public async Task ARefusedExchange_IsRefusedAsync()
    {
        ProviderTransport transport = new(
            Discovery(),
            Keys(),
            """{"error":"invalid_grant"}""",
            HttpStatusCode.BadRequest);

        SocialAuthentication result = await Core(transport).AuthenticateAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(SocialAuthenticationOutcome.ExchangeRefused, result.Outcome);
    }

    /// <summary>
    /// A provider that publishes no token endpoint cannot be exchanged with — Facebook's shape, ADR-0008.
    /// </summary>
    /// <remarks>
    /// Measured in ADR-0008 on 2026-09-27: Facebook's discovery document carries no `token_endpoint` and no
    /// `code` response type. The core says so rather than posting a form to nowhere, which is what makes
    /// Facebook a second **acquisition** rather than a second integration.
    /// </remarks>
    [Fact]
    public async Task AProviderWithNoTokenEndpoint_IsRefusedAsync()
    {
        ProviderTransport transport = new(
            $$"""{"jwks_uri":"{{_issuer}}/jwks"}""",
            Keys(),
            TokenBody(Token()));

        SocialAuthentication result = await Core(transport).AuthenticateAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(SocialAuthenticationOutcome.ExchangeRefused, result.Outcome);
    }

    /// <summary>The core names no provider: two rows, one implementation, the same answer.</summary>
    /// <remarks>
    /// The owner's requirement of 2026-09-27 was one relying-party implementation driven by configuration.
    /// This is what proves it: the second row differs only in its name, and nothing in the path behaves
    /// differently for it.
    /// </remarks>
    [Fact]
    public async Task TwoRows_RunTheSameCoreAsync()
    {
        ProviderTransport transport = new(Discovery(), Keys(), TokenBody(Token()));

        ISocialIdentityProvider core = Core(transport, "google", "second");

        SocialAuthentication google = await core.AuthenticateAsync(
            new SocialAuthenticationRequest("google", "code", "verifier", "https://app.example.com/callback"),
            TestContext.Current.CancellationToken);

        SocialAuthentication second = await core.AuthenticateAsync(
            new SocialAuthenticationRequest("second", "code", "verifier", "https://app.example.com/callback"),
            TestContext.Current.CancellationToken);

        Assert.Equal(SocialAuthenticationOutcome.Authenticated, google.Outcome);
        Assert.Equal(SocialAuthenticationOutcome.Authenticated, second.Outcome);
        Assert.Equal("google", google.Identity!.Provider);
        Assert.Equal("second", second.Identity!.Provider);
    }

    private static async Task<SocialAuthentication> AuthenticateAsync(string idToken)
    {
        ProviderTransport transport = new(Discovery(), Keys(), TokenBody(idToken));

        return await Core(transport).AuthenticateAsync(Request(), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The core, resolved through the public registration rather than constructed.
    /// </summary>
    /// <remarks>
    /// `OpenIdConnectIdentityProvider` is internal, and widening a type for a test is the wrong trade — the
    /// mail and catalog suites reach their adapters the same way. What this costs is one named-client
    /// override; what it buys is a test of the registration the application actually runs.
    /// <para>
    /// The client is named rather than defaulted: a default applies before a named client's own configuration
    /// and loses to it, which is how `BrevoMailServiceTests` once reached the real api.brevo.com.
    /// </para>
    /// </remarks>
    private static ISocialIdentityProvider Core(ProviderTransport transport, params string[] names)
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddInfrastructureServices(Options(names.Length == 0 ? ["google"] : names));

        // `AddHttpClient<ISocialIdentityProvider>` names its client after the service type.
        services.AddHttpClient("ISocialIdentityProvider")
            .ConfigurePrimaryHttpMessageHandler(() => transport);

        return services.BuildServiceProvider().GetRequiredService<ISocialIdentityProvider>();
    }

    private static InfrastructureOptions Options(IReadOnlyList<string> names) =>
        new(
            _bcryptWorkFactor,
            IsDevelopment: true,
            new MailOptions(MailOptions.LoggingProvider, null, null, null, null, null),
            new CatalogOptions(
                null,
                TrackRef.DefaultLifetime,
                TrackSearchCache.DefaultLifetime,
                CatalogOptions.DefaultMemoryLifetime,
                CatalogOptions.DefaultRequestTimeout),
            new JwtOptions(new string('k', JwtOptions.MinimumSigningKeyBytes), "tests", "tests", 15),
            new CredentialAttemptLimits(
                CredentialAttemptLimits.DefaultPermitsPerWindow,
                TimeSpan.FromMinutes(CredentialAttemptLimits.DefaultWindowMinutes),
                CredentialAttemptLimits.DefaultMaximumPenaltyWindows),
            [.. names.Select(Row)]);

    private static SocialProviderOptions Row(string name) =>
        new(name, new Uri(_discoveryPath), _issuer, _clientId, "a-client-secret");

    private static SocialAuthenticationRequest Request() =>
        new("google", "an-authorization-code", "a-verifier", "https://app.example.com/callback");

    private static string Discovery() =>
        $$"""{"token_endpoint":"{{_issuer}}/token","jwks_uri":"{{_issuer}}/jwks"}""";

    private static string Keys()
    {
        JsonWebKey key = JsonWebKeyConverter.ConvertFromRSASecurityKey(_signingKey);
        key.Use = "sig";
        key.Alg = SecurityAlgorithms.RsaSha256;

        return $$"""{"keys":[{{JsonSerializer.Serialize(key)}}]}""";
    }

    private static string TokenBody(string idToken) =>
        $$"""{"id_token":"{{idToken}}"}""";

    /// <summary>What a forged token differs from a valid one by — one knob per check.</summary>
    /// <param name="Issuer">The `iss`.</param>
    /// <param name="Audience">The `aud`.</param>
    /// <param name="Subject">The `sub`, or null for a token with none.</param>
    /// <param name="EmailVerified">The `email_verified`.</param>
    /// <param name="Expires">When it expires.</param>
    /// <param name="Key">Which key signs it.</param>
    /// <remarks>
    /// A record rather than six parameters: S107 caps a method at five, and a bag of defaults reads better
    /// at the call site anyway — `Token(new(Audience: "somebody-else"))` says what the test is about.
    /// </remarks>
    private sealed record Forgery(
        string Issuer = _issuer,
        string Audience = _clientId,
        string? Subject = _subject,
        bool EmailVerified = true,
        DateTime? Expires = null,
        RsaSecurityKey? Key = null);

    private static string Token(Forgery? forgery = null)
    {
        Forgery shape = forgery ?? new Forgery();

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            ["email"] = "person@example.com",
            ["email_verified"] = shape.EmailVerified,
            ["name"] = "A Person",
        };

        if (shape.Subject is not null)
        {
            claims["sub"] = shape.Subject;
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = shape.Issuer,
            Audience = shape.Audience,
            Claims = claims,
            Expires = shape.Expires ?? DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(
                shape.Key ?? _signingKey,
                SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>The cost the other suites use; nothing here hashes a password.</summary>
    private const int _bcryptWorkFactor = 4;
}
