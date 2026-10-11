using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// Proves the SDD §13.1 token properties against the real handler: what the token says, how long it lives,
/// and that the signature is RS256 over the key this API publishes — SPEC-050.
/// </summary>
public sealed class JwtAccessTokenIssuerTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string Issuer = "musicroom-tests";
    internal const string Audience = "musicroom-tests-client";
    internal const int AccessTokenMinutes = 15;

    /// <summary>The refresh-token family a test token belongs to — it becomes the `sid` claim.</summary>
    internal static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime _nowUtc = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonWebTokenHandler _handler = new();

    /// <summary>The payload is the account and the instants, and nothing that identifies a person.</summary>
    [Fact]
    public void Issue_CarriesSubIatExpAndJti_AndNothingSensitive()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        // Act
        JsonWebToken token = Issue(userId);

        // Assert
        Assert.Equal(userId.ToString(), token.Subject);
        Assert.NotEmpty(token.Id);
        Assert.Equal(Issuer, token.Issuer);
        Assert.Contains(Audience, token.Audiences, StringComparer.Ordinal);

        // A JWT is signed, not secret: whoever holds it reads it.
        Assert.DoesNotContain(
            token.Claims,
            claim => claim.Type is "email" or "name" or "display_name" or "password_hash");
    }

    /// <summary>
    /// The token names the session it belongs to, which is what lets logout end this one and no other —
    /// SPEC-010 escalation 1.
    /// </summary>
    [Fact]
    public void Issue_CarriesTheSessionIdAsTheSidClaim()
    {
        // Act
        JsonWebToken token = Issue(Guid.NewGuid());

        // Assert
        Assert.Equal(SessionId.ToString(), token.GetClaim("sid").Value);
    }

    /// <summary>15 minutes, from the instant the caller passed in — SDD §13.1.</summary>
    [Fact]
    public void Issue_ExpiresFifteenMinutesAfterTheIssuedInstant()
    {
        // Act
        JsonWebToken token = Issue(Guid.NewGuid());

        // Assert
        Assert.Equal(_nowUtc, token.IssuedAt);
        Assert.Equal(_nowUtc.AddMinutes(AccessTokenMinutes), token.ValidTo);
    }

    /// <summary>The lifetime the caller is told matches the lifetime that was signed.</summary>
    [Fact]
    public void Issue_ReportsTheSameLifetimeItSigned()
    {
        // Arrange
        const int secondsPerMinute = 60;
        JwtAccessTokenIssuer issuer = new(Options(), Keys());

        // Act
        IssuedAccessToken issued = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), _nowUtc);

        // Assert
        Assert.Equal(AccessTokenMinutes * secondsPerMinute, issued.ExpiresInSeconds);
    }

    /// <summary>The token is RS256, and its header names the key the JWKS publishes — SPEC-050.</summary>
    /// <remarks>
    /// The `kid` is what lets a verifier pick one key out of a published set, which is the whole point of
    /// signing an access token with a pair rather than a shared secret.
    /// </remarks>
    [Fact]
    public void Issue_SignsWithRs256AndNamesTheKey()
    {
        // Arrange
        RsaSigningKeyProvider keys = Keys();

        // Act
        JsonWebToken token = _handler.ReadJsonWebToken(
            new JwtAccessTokenIssuer(Options(), keys).Issue(Guid.NewGuid(), SessionId, _nowUtc).Token);

        // Assert
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Alg);
        Assert.Equal(keys.Current.KeyId, token.Kid);
    }

    /// <summary>
    /// A verifier given only the published key set accepts it; one given another key does not.
    /// </summary>
    /// <remarks>
    /// The second half is the assertion that matters: "signed by something" is not the claim, "signed by
    /// this issuer's key" is. The validation below is handed the JWKS and nothing else.
    /// </remarks>
    [Fact]
    public async Task Issue_ValidatesAgainstThePublishedKeySetOnlyAsync()
    {
        // Arrange
        RsaSigningKeyProvider keys = Keys();
        IssuedAccessToken issued = new JwtAccessTokenIssuer(Options(), keys)
            .Issue(Guid.NewGuid(), SessionId, DateTime.UtcNow);

        // Act
        TokenValidationResult accepted = await _handler.ValidateTokenAsync(issued.Token, Parameters(keys));
        TokenValidationResult refused = await _handler.ValidateTokenAsync(issued.Token, Parameters(Keys()));

        // Assert
        Assert.True(accepted.IsValid, accepted.Exception?.Message);
        Assert.False(refused.IsValid);
    }

    /// <summary>
    /// During a rotation overlap, a token signed by the outgoing key still validates.
    /// </summary>
    /// <remarks>
    /// What SPEC-043's second slot was built for, asserted on the access token rather than only on the key
    /// provider: the keys the handler accepts are every key the JWKS publishes, so a rotation does not
    /// invalidate what the previous key signed.
    /// </remarks>
    [Fact]
    public async Task ARotationOverlap_KeepsTheOutgoingKeyAcceptedAsync()
    {
        // Arrange — a token from the key that is about to become the previous one.
        string outgoing = Pem();
        RsaSigningKeyProvider before = new(new OneKeySecretProvider(outgoing));

        IssuedAccessToken issued = new JwtAccessTokenIssuer(Options(), before)
            .Issue(Guid.NewGuid(), SessionId, DateTime.UtcNow);

        // The rotation: a new key signs, the outgoing one is published and accepted.
        RsaSigningKeyProvider after = new(new OneKeySecretProvider(Pem(), outgoing));

        // Act
        TokenValidationResult result = await _handler.ValidateTokenAsync(issued.Token, Parameters(after));

        // Assert
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(2, after.Verification.Count);
    }

    /// <summary>A lifetime of zero or less would issue tokens that are already expired.</summary>
    [Fact]
    public void JwtOptions_WithANonPositiveLifetime_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new JwtOptions(Issuer, Audience, 0));

    /// <summary>An issuer or audience nobody configured is refused at start-up.</summary>
    [Fact]
    public void JwtOptions_WithoutAnIssuerOrAudience_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JwtOptions(" ", Audience, AccessTokenMinutes));
        Assert.Throws<ArgumentException>(() => new JwtOptions(Issuer, " ", AccessTokenMinutes));
    }

    private static JwtOptions Options() => new(Issuer, Audience, AccessTokenMinutes);

    /// <summary>A fresh key pair, as a host reads one out of configuration.</summary>
    private static RsaSigningKeyProvider Keys() => new(new OneKeySecretProvider(Pem()));

    /// <summary>One PEM private key.</summary>
    private static string Pem()
    {
        using RSA rsa = RSA.Create(2048);

        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Answers the current key variable and nothing else.</summary>
    private sealed class OneKeySecretProvider : ISecretProvider
    {
        private readonly string _pem;

        private readonly string? _previous;

        internal OneKeySecretProvider(string pem, string? previous = null)
        {
            _pem = pem;
            _previous = previous;
        }

        public string Require(string name) => Read(name) ?? throw new InvalidOperationException(name);

        public string? Read(string name) => name switch
        {
            RsaSigningKeyProvider.CurrentKeyVariable => _pem,
            RsaSigningKeyProvider.PreviousKeyVariable => _previous,
            _ => null,
        };

        public int ReadInteger(string name, int fallback) => fallback;

        public bool ReadBoolean(string name, bool fallback) => fallback;
    }

    private static JsonWebToken Issue(Guid userId)
    {
        JwtAccessTokenIssuer issuer = new(Options(), Keys());

        return _handler.ReadJsonWebToken(issuer.Issue(userId, SessionId, _nowUtc).Token);
    }

    private static TokenValidationParameters Parameters(ISigningKeyProvider keys) =>
        JwtBearerConfiguration.Build(Options(), keys);
}
