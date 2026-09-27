using System.Buffers.Text;
using System.Security.Cryptography;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The one definition of how a password reset token is minted — SDD §13.1.
/// </summary>
/// <remarks>
/// Written beside <see cref="VerificationTokenIssuer"/> rather than folded into it: the two share an
/// algorithm and differ in the one thing that matters, the window. One method with a lifetime
/// parameter would let a caller mint a 24-hour reset token by passing the wrong argument.
/// </remarks>
internal static class PasswordResetTokenIssuer
{
    /// <summary>Mints a fresh single-use reset token for an account.</summary>
    /// <param name="userId">The account whose password the token replaces.</param>
    /// <param name="nowUtc">The instant the window is measured from.</param>
    /// <returns>The row to insert and the plaintext the recipient uses.</returns>
    internal static IssuedPasswordResetToken Issue(Guid userId, DateTime nowUtc)
    {
        // 256 bits of entropy, which is what the token needs to be unguessable.
        const int tokenByteLength = 32;

        // One hour — authentication-authorization, and shorter than verification because this token
        // replaces a credential rather than proving an address.
        const int tokenLifetimeHours = 1;

        // base64url, so the value survives a URL untouched: no padding, no characters to escape.
        string plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(tokenByteLength));

        PasswordResetToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // The row holds the digest. The plaintext leaves this method only to reach the mail
            // sender, and a leaked database therefore hands nobody a working reset.
            TokenHash = OpaqueTokenHash.Of(plaintext),
            ExpiresAtUtc = nowUtc.AddHours(tokenLifetimeHours),
            ConsumedAtUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        return new IssuedPasswordResetToken(token, plaintext);
    }
}

/// <summary>
/// The stored token and the plaintext that must reach the mail sender, kept together for the one call
/// that needs both.
/// </summary>
/// <param name="Token">The row to insert, holding only the digest.</param>
/// <param name="Plaintext">The value the recipient submits. Never stored and never logged.</param>
internal sealed record IssuedPasswordResetToken(PasswordResetToken Token, string Plaintext);
