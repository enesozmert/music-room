using System.Buffers.Text;
using System.Security.Cryptography;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The token pair <c>POST /auth/sessions</c> and <c>POST /auth/tokens</c> answer with —
/// <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="AccessToken">The signed, short-lived JWT.</param>
/// <param name="RefreshToken">The opaque refresh token, in plaintext. This is the only moment it
/// exists outside the caller's own storage: the database keeps its digest, and nothing logs it.</param>
/// <param name="ExpiresInSeconds">The access token's lifetime, for the client's own planning.</param>
public sealed record AuthTokenPair(string AccessToken, string RefreshToken, int ExpiresInSeconds);

/// <summary>
/// Mints the plaintext of a refresh token.
/// </summary>
/// <remarks>
/// Login and rotation both issue one, and they must issue the same shape, so the generator is written
/// once. It shares this file with the pair it half fills because SPEC-005's Files table is the
/// contract for what this change may create.
/// </remarks>
internal static class RefreshTokenSecret
{
    /// <summary>256 bits of entropy, which is what an opaque bearer token needs to be unguessable.</summary>
    internal const int ByteLength = 32;

    /// <summary>A fresh random token.</summary>
    /// <returns>base64url, so the value survives a URL and a JSON body untouched: no padding, no
    /// characters to escape.</returns>
    internal static string Create() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));
}
