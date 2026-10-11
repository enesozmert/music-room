using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Configuration;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// Reads the RS256 key pair this API signs with — ADR-0031, issue O19, SPEC-043.
/// </summary>
/// <remarks>
/// **Nothing here generates a key.** A system that makes its own signing key on first run has a key nobody
/// backed up, nobody can rotate and nobody else can verify against after a restart. A missing key is a
/// refusal at start-up naming the variable and the command that prints one — `make oidc-key`.
/// <para>
/// **Two slots, because rotation needs an overlap.** `OIDC_SIGNING_KEY` signs; `OIDC_SIGNING_KEY_PREVIOUS`,
/// when present, is published and accepted and never signs. Rotating is therefore: move the current key into
/// the previous slot, put a new one in the current slot, and remove the previous slot once nothing it signed
/// is still alive. Without the second slot, rotating invalidates every token in flight — which is the whole
/// of what O19 asked to be designed rather than discovered.
/// </para>
/// </remarks>
public sealed class RsaSigningKeyProvider : ISigningKeyProvider
{
    /// <summary>The configuration key holding the PEM private key that signs.</summary>
    public const string CurrentKeyVariable = "OIDC_SIGNING_KEY";

    /// <summary>The configuration key holding the outgoing PEM private key, during an overlap.</summary>
    public const string PreviousKeyVariable = "OIDC_SIGNING_KEY_PREVIOUS";

    /// <summary>The smallest modulus this provider accepts.</summary>
    /// <remarks>
    /// 2048 bits, which is the floor every current recommendation names for RSA. Smaller is refused rather
    /// than warned about: a key that is accepted with a warning is a key that ships.
    /// </remarks>
    public const int MinimumKeySizeBits = 2048;

    /// <summary>Reads both slots and computes their identifiers.</summary>
    /// <param name="secrets">Where configuration comes from — SPEC-030's one abstraction.</param>
    /// <exception cref="InvalidOperationException">
    /// No key is configured, or one of the two is not a usable RSA private key of at least
    /// <see cref="MinimumKeySizeBits"/> bits.
    /// </exception>
    public RsaSigningKeyProvider(ISecretProvider secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        Current = Read(secrets, CurrentKeyVariable, required: true)!;

        SigningKeyMaterial? previous = Read(secrets, PreviousKeyVariable, required: false);

        Verification = previous is null ? [Current] : [Current, previous];
    }

    /// <inheritdoc />
    public SigningKeyMaterial Current { get; }

    /// <inheritdoc />
    public IReadOnlyList<SigningKeyMaterial> Verification { get; }

    /// <summary>The JSON Web Key Set a verifier reads, public halves only.</summary>
    /// <returns>The set, as the `.well-known/jwks.json` route serves it.</returns>
    /// <remarks>
    /// Built here rather than in the endpoint because the rule it enforces is this class's own: **the private
    /// half never leaves**. `JsonWebKeyConverter` on a key holding a private exponent would publish it, so
    /// every key is re-imported from its public parameters before it is converted.
    /// </remarks>
    public JsonWebKeySet PublicKeySet()
    {
        JsonWebKeySet set = new();

        foreach (SigningKeyMaterial material in Verification)
        {
            RSA rsa = RSA.Create();
            rsa.ImportParameters(material.Key.Rsa.ExportParameters(includePrivateParameters: false));

            JsonWebKey key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa));
            key.Kid = material.KeyId;
            key.Use = "sig";
            key.Alg = SecurityAlgorithms.RsaSha256;

            set.Keys.Add(key);
        }

        return set;
    }

    private static SigningKeyMaterial? Read(ISecretProvider secrets, string variable, bool required)
    {
        string? pem = secrets.Read(variable);

        if (pem is null)
        {
            return required
                ? throw new InvalidOperationException(
                    $"{variable} is not set. This API signs identity tokens with RS256 and does not generate "
                    + "its own key — a generated key is one nobody backed up. Run `make oidc-key` and put the "
                    + "result in .env. See ADR-0031.")
                : null;
        }

        RSA rsa = RSA.Create();

        try
        {
            // A PEM key in an environment variable is one line with `\n` written out, because that is the
            // only shape `.env`, `docker compose` and a hosting platform's secret field all carry without
            // mangling. Two literal characters are not a line break, and `ImportFromPem` says so with an
            // unhelpful `ArgumentException` — so the escape is undone here rather than discovered at boot.
            rsa.ImportFromPem(pem.Replace("\\n", "\n", StringComparison.Ordinal));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{variable} is not a PEM private key. Run `make oidc-key` for one.", exception);
        }

        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new InvalidOperationException(
                $"{variable} is {rsa.KeySize} bits; RS256 needs at least {MinimumKeySizeBits}.");
        }

        string keyId = Thumbprint(rsa);

        // The identifier on the key itself, not only beside it: `JsonWebTokenHandler` reads it to write the
        // `kid` header of everything it signs, and a token whose header names no key forces a verifier to
        // try every published one — which it cannot do meaningfully during a rotation overlap. SPEC-044.
        RsaSecurityKey key = new(rsa) { KeyId = keyId };

        return new SigningKeyMaterial(keyId, key);
    }

    /// <summary>The `kid`: a base64url SHA-256 thumbprint of the public key, per RFC 7638's shape.</summary>
    /// <remarks>
    /// Computed rather than configured, so two hosts given the same key agree without being told, and a
    /// rotated key cannot inherit the identifier of the one it replaced.
    /// </remarks>
    private static string Thumbprint(RSA rsa)
    {
        RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: false);

        string json = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
            ["kty"] = "RSA",
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
        });

        return Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    }
}
