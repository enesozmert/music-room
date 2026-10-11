using System.Buffers.Text;
using System.Security.Cryptography;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The one definition of how an email verification credential is minted — SDD §10.1 step 4, SPEC-052.
/// </summary>
/// <remarks>
/// Registration and resend must issue the same shape of credential, because one endpoint spends what
/// either of them produced. A copy in each feature folder is a bug waiting for one of them to be
/// changed — the same reasoning as <see cref="OpaqueTokenHash"/>, which this method calls.
/// <para>
/// **Two credentials, one row** — SPEC-052. A 256-bit token for the link and a six-character code for a
/// person who would rather type; both hashed, both spent together, both expiring at the same instant.
/// Reusing the code inside the link would quietly reduce the link to six characters.
/// </para>
/// </remarks>
internal static class VerificationTokenIssuer
{
    /// <summary>Mints a fresh single-use token and code for an account.</summary>
    /// <param name="userId">The account the credential verifies.</param>
    /// <param name="nowUtc">The instant the window is measured from.</param>
    /// <returns>The row to insert, the plaintext the recipient clicks and the code they may type.</returns>
    internal static IssuedVerificationToken Issue(Guid userId, DateTime nowUtc)
    {
        // 256 bits of entropy, which is what the token needs to be unguessable.
        const int tokenByteLength = 32;

        // Fifteen minutes — SPEC-052 decision 4, down from 24 hours. Right for something typed out of an
        // inbox, and the cost is stated rather than hidden: a mail delayed past the window needs a resend,
        // and resend is rate-limited.
        const int lifetimeMinutes = 15;

        // base64url, so the value survives a URL untouched: no padding, no characters to escape.
        string plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(tokenByteLength));

        string code = VerificationCode.Create();

        EmailVerificationToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // The row holds the digest. The plaintext leaves this method only to reach the mail
            // sender, and a leaked database therefore hands nobody a working link.
            TokenHash = OpaqueTokenHash.Of(plaintext),

            // The code's digest sits beside the token's, and the attempt budget starts at zero. Five wrong
            // codes spend the row — the thing that makes six characters safe.
            CodeHash = OpaqueTokenHash.Of(code),
            Attempts = 0,
            ExpiresAtUtc = nowUtc.AddMinutes(lifetimeMinutes),
            ConsumedAtUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        return new IssuedVerificationToken(token, plaintext, code);
    }
}

/// <summary>
/// The stored token and the plaintext that must reach the mail sender, kept together for the one
/// call that needs both.
/// </summary>
/// <param name="Token">The row to insert, holding only the two digests.</param>
/// <param name="Plaintext">The value the recipient clicks. Never stored and never logged.</param>
/// <param name="Code">The six characters the recipient may type instead. Never stored and never logged.</param>
internal sealed record IssuedVerificationToken(
    EmailVerificationToken Token,
    string Plaintext,
    string Code);
