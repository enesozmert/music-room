using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// The signing identity — ADR-0031, issue O19, SPEC-043.
/// </summary>
/// <remarks>
/// Four things nothing else can check: a PEM in configuration becomes a usable key, the `kid` is computed
/// rather than chosen, a rotation publishes both keys while only one signs, and a missing or unusable key
/// stops the host rather than being replaced by one nobody backed up.
/// </remarks>
public sealed class RsaSigningKeyProviderTests
{
    /// <summary>A key in configuration becomes the key this API signs with.</summary>
    [Fact]
    public void AConfiguredKey_IsTheSigningKey()
    {
        RsaSigningKeyProvider provider = new(Secrets(Pem()));

        Assert.NotNull(provider.Current.Key);
        Assert.True(provider.Current.Key.Rsa.KeySize >= RsaSigningKeyProvider.MinimumKeySizeBits);
    }

    /// <summary>
    /// The same key gives the same `kid` on any host, because it is the key's own thumbprint.
    /// </summary>
    /// <remarks>
    /// The point of computing it: two instances given one key agree without being told, and a rotated key
    /// cannot inherit the identifier of the one it replaced. A configured name would let both go wrong.
    /// </remarks>
    [Fact]
    public void TheKeyId_IsTheKeysOwnThumbprint()
    {
        string pem = Pem();

        RsaSigningKeyProvider first = new(Secrets(pem));
        RsaSigningKeyProvider second = new(Secrets(pem));

        Assert.Equal(first.Current.KeyId, second.Current.KeyId);
        Assert.NotEqual(first.Current.KeyId, new RsaSigningKeyProvider(Secrets(Pem())).Current.KeyId);
    }

    /// <summary>A rotation publishes both keys and signs with one — the overlap O19 asks for.</summary>
    [Fact]
    public void ARotation_PublishesBothKeysAndSignsWithOne()
    {
        string current = Pem();
        string previous = Pem();

        RsaSigningKeyProvider provider = new(Secrets(current, previous));

        Assert.Equal(2, provider.Verification.Count);
        Assert.Equal(provider.Current.KeyId, provider.Verification[0].KeyId);
        Assert.NotEqual(provider.Current.KeyId, provider.Verification[1].KeyId);
        Assert.Equal(2, provider.PublicKeySet().Keys.Count);
    }

    /// <summary>The published set carries no private material.</summary>
    /// <remarks>
    /// Asserted by absence, which is the only way to assert this: `JsonWebKeyConverter` on a key that holds a
    /// private exponent publishes it, and the difference between a correct JWKS and a catastrophic one is one
    /// argument. `D` is RSA's private exponent; `P` and `Q` are its primes.
    /// </remarks>
    [Fact]
    public void ThePublishedSet_CarriesNoPrivateMaterial()
    {
        JsonWebKeySet set = new RsaSigningKeyProvider(Secrets(Pem())).PublicKeySet();

        JsonWebKey key = Assert.Single(set.Keys);

        Assert.Null(key.D);
        Assert.Null(key.P);
        Assert.Null(key.Q);
        Assert.NotNull(key.N);
        Assert.Equal("RS256", key.Alg);
    }

    /// <summary>A missing key stops the host, naming the variable and the command that makes one.</summary>
    [Fact]
    public void AMissingKey_RefusesWithTheCommandThatMakesOne()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => new RsaSigningKeyProvider(Secrets(null)));

        Assert.Contains(RsaSigningKeyProvider.CurrentKeyVariable, refused.Message, StringComparison.Ordinal);
        Assert.Contains("make oidc-key", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Something that is not a key is refused rather than half-accepted.</summary>
    [Fact]
    public void AMalformedKey_IsRefused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => new RsaSigningKeyProvider(Secrets("-----BEGIN PRIVATE KEY-----not-a-key-----END PRIVATE KEY-----")));

        Assert.Contains(RsaSigningKeyProvider.CurrentKeyVariable, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A key below the floor is refused rather than warned about.</summary>
    /// <remarks>
    /// 1024 bits still produces a working RS256 signature, which is exactly why this has to be a refusal: a
    /// weak key that signs is a weak key that ships.
    /// </remarks>
    [Fact]
    public void AKeyBelowTheFloor_IsRefused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => new RsaSigningKeyProvider(Secrets(Pem(bits: 1024))));

        Assert.Contains("1024", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key written as one line with escaped newlines loads, which is how `.env` carries PEM.
    /// </summary>
    /// <remarks>
    /// `.env`, `docker compose` and a hosting platform's secret field all carry one line cleanly and a
    /// multi-line value badly, so `make oidc-key` prints `\n` and this undoes it. Without the unescape the
    /// failure is an `ArgumentException` at boot that names nothing useful.
    /// </remarks>
    [Fact]
    public void AKeyWithEscapedNewlines_Loads()
    {
        string oneLine = Pem().ReplaceLineEndings("\\n");

        RsaSigningKeyProvider provider = new(Secrets(oneLine));

        Assert.NotNull(provider.Current.Key);
    }

    private static string Pem(int bits = 2048)
    {
        using RSA rsa = RSA.Create(bits);

        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static ISecretProvider Secrets(string? current, string? previous = null) =>
        new StubSecretProvider(current, previous);

    /// <summary>Answers the two key variables and nothing else.</summary>
    private sealed class StubSecretProvider : ISecretProvider
    {
        private readonly string? _current;
        private readonly string? _previous;

        internal StubSecretProvider(string? current, string? previous)
        {
            _current = current;
            _previous = previous;
        }

        public string Require(string name) => Read(name) ?? throw new InvalidOperationException(name);

        public string? Read(string name) => name switch
        {
            RsaSigningKeyProvider.CurrentKeyVariable => _current,
            RsaSigningKeyProvider.PreviousKeyVariable => _previous,
            _ => null,
        };

        public int ReadInteger(string name, int fallback) => fallback;

        public bool ReadBoolean(string name, bool fallback) => fallback;
    }
}
