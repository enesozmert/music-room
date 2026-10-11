using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Identity;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Identity;

/// <summary>
/// The ID token — ADR-0031's claim list, SPEC-044 decision 4.
/// </summary>
/// <remarks>
/// Four things nothing else checks: the claims are the ones ADR-0031 names, a `nonce` is echoed rather than
/// invented, the signature verifies against the published key set, and the header names the key that signed
/// it so a rotation stays survivable.
/// </remarks>
public sealed class IdTokenIssuerTests
{
    private const string _clientId = "musicroom-app";
    private const string _issuer = "https://api.example.test";

    private static readonly DateTime _now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _authenticated = _now.AddMinutes(-3);

    /// <summary>The token carries exactly what ADR-0031 lists, and says who it is for.</summary>
    [Fact]
    public async Task TheToken_CarriesTheClaimsAdr0031NamesAsync()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        RsaSigningKeyProvider keys = Keys();

        // Act
        string token = new IdTokenIssuer(keys).Issue(Request(userId, nonce: null), _now);

        // Assert
        JsonWebToken read = new(token);

        Assert.Equal(_issuer, read.Issuer);
        Assert.Equal(_clientId, Assert.Single(read.Audiences));
        Assert.Equal(userId.ToString(), read.Subject);
        Assert.Equal(_now, read.IssuedAt);
        Assert.Equal(_now.AddMinutes(IdTokenIssuer.LifetimeMinutes), read.ValidTo);

        // `auth_time` is seconds since the epoch, which is what OpenID Connect Core 1.0 §2 defines it as.
        Assert.Equal(
            new DateTimeOffset(_authenticated, TimeSpan.Zero).ToUnixTimeSeconds(),
            read.GetPayloadValue<long>("auth_time"));

        // No address and no display name: an ID token travels through a redirect and is signed, not secret.
        Assert.False(read.TryGetClaim("email", out _));
        Assert.False(read.TryGetClaim("name", out _));

        await Task.CompletedTask;
    }

    /// <summary>A nonce is echoed when the client sent one, and absent when it did not.</summary>
    /// <remarks>
    /// The absent half matters as much as the present one: a nonce this API invented would satisfy no check a
    /// client could make, and a client that sent none has nothing to compare against.
    /// </remarks>
    [Fact]
    public void ANonce_IsEchoedAndNeverInvented()
    {
        IdTokenIssuer issuer = new(Keys());

        JsonWebToken withNonce = new(issuer.Issue(Request(Guid.NewGuid(), "client-chose-this"), _now));
        JsonWebToken withoutNonce = new(issuer.Issue(Request(Guid.NewGuid(), nonce: null), _now));

        Assert.Equal("client-chose-this", withNonce.GetClaim("nonce").Value);
        Assert.False(withoutNonce.TryGetClaim("nonce", out _));
    }

    /// <summary>A verifier given only the published key set accepts the token.</summary>
    /// <remarks>
    /// The point of being a provider: something that has never spoken to this API can check what it said. The
    /// validation below is handed the JWKS and the issuer, and nothing from inside this process.
    /// </remarks>
    [Fact]
    public async Task TheSignature_VerifiesAgainstThePublishedKeySetAsync()
    {
        // Arrange
        RsaSigningKeyProvider keys = Keys();
        string token = new IdTokenIssuer(keys).Issue(Request(Guid.NewGuid(), nonce: null), DateTime.UtcNow);

        string published = System.Text.Json.JsonSerializer.Serialize(keys.PublicKeySet());

        // Act
        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                ValidIssuer = _issuer,
                ValidAudience = _clientId,
                IssuerSigningKeys = new JsonWebKeySet(published).GetSigningKeys(),
                ValidateLifetime = true,
            });

        // Assert
        Assert.True(result.IsValid, result.Exception?.Message);
    }

    /// <summary>The header names the key that signed it, with the `kid` the JWKS publishes.</summary>
    /// <remarks>
    /// Without it a verifier has to try every published key, and during a rotation overlap it cannot tell
    /// which of two keys signed a token it is holding — which is the whole reason the overlap exists.
    /// </remarks>
    [Fact]
    public void TheHeader_NamesTheKeyThatSignedIt()
    {
        RsaSigningKeyProvider keys = Keys();

        JsonWebToken read = new(new IdTokenIssuer(keys).Issue(Request(Guid.NewGuid(), null), _now));

        Assert.Equal(keys.Current.KeyId, read.Kid);
        Assert.Equal(SecurityAlgorithms.RsaSha256, read.Alg);
    }

    private static IdTokenRequest Request(Guid userId, string? nonce) =>
        new(_issuer, _clientId, userId, _authenticated, nonce);

    private static RsaSigningKeyProvider Keys()
    {
        using RSA rsa = RSA.Create(2048);

        return new RsaSigningKeyProvider(new OneKeySecretProvider(rsa.ExportPkcs8PrivateKeyPem()));
    }

    /// <summary>Answers the current key variable and nothing else.</summary>
    private sealed class OneKeySecretProvider : ISecretProvider
    {
        private readonly string _pem;

        internal OneKeySecretProvider(string pem) => _pem = pem;

        public string Require(string name) => Read(name) ?? throw new InvalidOperationException(name);

        public string? Read(string name) =>
            name == RsaSigningKeyProvider.CurrentKeyVariable ? _pem : null;

        public int ReadInteger(string name, int fallback) => fallback;

        public bool ReadBoolean(string name, bool fallback) => fallback;
    }
}
