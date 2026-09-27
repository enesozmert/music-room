using System.Security.Cryptography;
using System.Text;

namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// The one definition of how an opaque token becomes the digest stored in a <c>token_hash</c>
/// column — email verification tokens and refresh tokens both.
/// </summary>
/// <remarks>
/// Issuing and spending must agree on this byte for byte, so every side calls this method instead of
/// repeating the algorithm. A copy in each feature folder is a bug waiting for one of them to be
/// changed.
/// <para>
/// It lives in <c>Abstractions/Security</c> rather than in a feature folder because two features now
/// depend on it. It moved here from <c>Features/Auth/Commands/VerifyEmail</c> unchanged — SPEC-005.
/// </para>
/// </remarks>
public static class OpaqueTokenHash
{
    /// <summary>The length of the value <see cref="Of"/> returns: SHA-256 in hex.</summary>
    public const int HexLength = 64;

    /// <summary>Hex-encoded SHA-256 of the token's UTF-8 bytes.</summary>
    /// <param name="token">The plaintext token, as issued and as submitted.</param>
    /// <returns>The 64-character digest the row is keyed by.</returns>
    /// <remarks>
    /// SHA-256 rather than a password hash on purpose: the token carries 256 bits of entropy from a
    /// cryptographic generator, so there is nothing to brute-force, and the lookup has to be a
    /// single indexed read — authentication-authorization.
    /// </remarks>
    public static string Of(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
