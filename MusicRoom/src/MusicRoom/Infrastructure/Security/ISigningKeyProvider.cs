using Microsoft.IdentityModel.Tokens;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The key this API signs identity assertions with, and every key one may be verified against —
/// ADR-0031, SPEC-043.
/// </summary>
/// <remarks>
/// Two members rather than one, and the difference is rotation. **One key signs**; **several verify**. A
/// rotated key that stopped verifying immediately would invalidate every token already in flight, so the
/// outgoing key stays in the published set until nothing it signed is still alive — the overlap window issue
/// **O19** asks for.
/// <para>
/// **In `Infrastructure`, not `Application`.** The first draft put it beside `IPasswordHasher`, which was
/// wrong twice: `Application` references no security library, so the abstraction would have dragged
/// `Microsoft.IdentityModel.Tokens` into the layer that is supposed to name no technology — and nothing in
/// `Application` signs anything. The two consumers are the token endpoint and the JWKS route, both outside it.
/// </para>
/// <para>
/// The implementation reads configuration through `ISecretProvider`, which is what makes ADR-0031's sentence
/// true without a rewrite: *"`EnvSecretProvider` holds it until ADR-0029 lands the vault at S1b, after which
/// the private key belongs there."* Moving the key is then a registration change.
/// </para>
/// </remarks>
public interface ISigningKeyProvider
{
    /// <summary>The key new tokens are signed with.</summary>
    SigningKeyMaterial Current { get; }

    /// <summary>
    /// Every key a token may legitimately carry: the current one, and the outgoing one while it overlaps.
    /// </summary>
    /// <remarks>
    /// This is what the JWKS publishes and what a verifier is given. Ordered current-first, because a
    /// verifier that tries keys in order should try the likely one first.
    /// </remarks>
    IReadOnlyList<SigningKeyMaterial> Verification { get; }

    /// <summary>The JSON Web Key Set a verifier reads, public halves only.</summary>
    /// <returns>The set, as the `.well-known/jwks.json` route serves it.</returns>
    /// <remarks>
    /// On the abstraction rather than on the implementation, because the rule it enforces is one the route
    /// must not be able to get wrong: **the private half never leaves**. An endpoint handed raw key material
    /// and left to convert it could publish a private exponent with one wrong argument.
    /// </remarks>
    JsonWebKeySet PublicKeySet();
}

/// <summary>One RSA key pair, as the signer and the JWKS both need it.</summary>
/// <param name="KeyId">
/// The `kid`, which is the SHA-256 thumbprint of the public key rather than a name somebody chose: two hosts
/// given the same key agree about it, and a rotated key cannot reuse its predecessor's identifier.
/// </param>
/// <param name="Key">The key material. Private for the current key; the public half is what the JWKS shows.</param>
public sealed record SigningKeyMaterial(string KeyId, RsaSecurityKey Key);
