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

    /// <summary>
    /// The SHA-256 digest of the six-character code, hex-encoded — SPEC-055. The code is never stored,
    /// exactly as the token is not.
    /// </summary>
    /// <remarks>
    /// A second credential on the same row rather than a second row, for the reason
    /// <see cref="EmailVerificationToken"/> gives: the link and the code prove the same thing about the
    /// same request, so spending either must spend both. Two rows would be two windows, and one of them
    /// would eventually outlive the other.
    /// </remarks>
    public required string CodeHash { get; set; }

    /// <summary>
    /// How many wrong codes have been offered against this row — SPEC-055 decision 2.
    /// </summary>
    /// <remarks>
    /// Five, as verification allows. The budget is the whole of what makes six characters acceptable in
    /// front of the strongest action in the system: a row at the limit does not match the consuming
    /// statement, so the sixth guess is refused even when it is right, and a sixth chance costs a new
    /// mail through an endpoint that is rate-limited per IP and per account — and that mail goes to the
    /// account's own address.
    /// </remarks>
    public int Attempts { get; set; }
}
