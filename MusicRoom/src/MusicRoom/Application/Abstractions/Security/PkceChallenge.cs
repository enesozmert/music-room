using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// The one definition of PKCE's <c>S256</c> transformation — RFC 7636 §4.2, SPEC-044 decision 2.
/// </summary>
/// <remarks>
/// Written once because the authorization endpoint stores a challenge and the token endpoint recomputes one,
/// and the two must agree byte for byte. A second copy is a bug waiting for one of them to be changed — the
/// same reasoning <see cref="OpaqueTokenHash"/> records.
/// </remarks>
public static class PkceChallenge
{
    /// <summary>The shortest verifier RFC 7636 §4.1 allows.</summary>
    public const int MinimumVerifierLength = 43;

    /// <summary>The longest verifier RFC 7636 §4.1 allows.</summary>
    public const int MaximumVerifierLength = 128;

    /// <summary>
    /// The length of an <c>S256</c> challenge: base64url of a 32-byte digest, unpadded.
    /// </summary>
    public const int ChallengeLength = 43;

    /// <summary>The challenge method this provider supports, and the only one.</summary>
    /// <remarks>
    /// <c>plain</c> is refused, not merely discouraged: a challenge equal to its verifier is a challenge
    /// whoever intercepted the authorization request can replay, which is the attack PKCE exists to stop.
    /// </remarks>
    public const string Method = "S256";

    /// <summary>base64url of the SHA-256 of the verifier's ASCII bytes, without padding.</summary>
    /// <param name="verifier">The <c>code_verifier</c> the client sent.</param>
    /// <returns>The value a conforming client would have sent as <c>code_challenge</c>.</returns>
    public static string Of(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Whether a verifier matches a stored challenge, compared in constant time.</summary>
    /// <param name="challenge">The challenge the code was issued against.</param>
    /// <param name="verifier">The verifier presented at the token endpoint.</param>
    /// <returns>True only for a match.</returns>
    /// <remarks>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> rather than <c>==</c>: the comparison is of a
    /// secret-derived value, and a comparison that returns early tells a caller how much of its guess was
    /// right. Lengths that differ are a mismatch, which is the one thing this cannot hide and does not
    /// need to.
    /// </remarks>
    public static bool Matches(string challenge, string verifier)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(verifier);

        if (verifier.Length is < MinimumVerifierLength or > MaximumVerifierLength)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Of(verifier)),
            Encoding.ASCII.GetBytes(challenge));
    }
}
