using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A single-use proof that the holder of an address asked to replace its password — SDD §13.1.
/// </summary>
/// <remarks>
/// The row holds the <em>hash</em> of the token, never the token, for the same reason
/// <see cref="EmailVerificationToken"/> does. The stakes are higher here: a verification token proves
/// an address, a reset token replaces a credential, so the window is one hour rather than 24
/// (`authentication-authorization`, ADR-0024).
/// </remarks>
public sealed class PasswordResetToken : Entity
{
    /// <summary>The account whose password this token replaces.</summary>
    public Guid UserId { get; set; }

    /// <summary>The SHA-256 digest of the token, hex-encoded. The token itself is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When the token stops being accepted. Always UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the token was spent, or null while it is unused. A second use is refused on this
    /// column, so one mail cannot hand out two takeovers.</summary>
    public DateTime? ConsumedAtUtc { get; set; }
}
