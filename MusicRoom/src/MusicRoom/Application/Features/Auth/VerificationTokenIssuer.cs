using System.Buffers.Text;
using System.Security.Cryptography;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The one definition of how an email verification token is minted — SDD §10.1 step 4.
/// </summary>
/// <remarks>
/// Registration and resend must issue the same shape of token, because one endpoint spends what
/// either of them produced. A copy in each feature folder is a bug waiting for one of them to be
/// changed — the same reasoning as <see cref="OpaqueTokenHash"/>, which this method calls.
/// </remarks>
internal static class VerificationTokenIssuer
{
    /// <summary>Mints a fresh single-use token for an account.</summary>
    /// <param name="userId">The account the token verifies.</param>
    /// <param name="nowUtc">The instant the window is measured from.</param>
    /// <returns>The row to insert and the plaintext the recipient clicks.</returns>
    internal static IssuedVerificationToken Issue(Guid userId, DateTime nowUtc)
    {
        // 256 bits of entropy, which is what the token needs to be unguessable.
        const int tokenByteLength = 32;

        // The verification window — authentication-authorization.
        const int tokenLifetimeHours = 24;

        // base64url, so the value survives a URL untouched: no padding, no characters to escape.
        string plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(tokenByteLength));

        EmailVerificationToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // The row holds the digest. The plaintext leaves this method only to reach the mail
            // sender, and a leaked database therefore hands nobody a working link.
            TokenHash = OpaqueTokenHash.Of(plaintext),
            ExpiresAtUtc = nowUtc.AddHours(tokenLifetimeHours),
            ConsumedAtUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        return new IssuedVerificationToken(token, plaintext);
    }
}

/// <summary>
/// The stored token and the plaintext that must reach the mail sender, kept together for the one
/// call that needs both.
/// </summary>
/// <param name="Token">The row to insert, holding only the digest.</param>
/// <param name="Plaintext">The value the recipient clicks. Never stored and never logged.</param>
internal sealed record IssuedVerificationToken(EmailVerificationToken Token, string Plaintext);
