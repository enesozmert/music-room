using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A single-use proof that the holder of an address asked for it to be verified — SDD §10.1 step 4.
/// </summary>
/// <remarks>
/// The row holds the <em>hash</em> of the token, never the token. A leaked database therefore does
/// not hand anyone a working verification link, which is the same reasoning the standard applies to
/// refresh tokens.
/// </remarks>
public sealed class EmailVerificationToken : Entity
{
    /// <summary>The account this token verifies.</summary>
    public Guid UserId { get; set; }

    /// <summary>The SHA-256 digest of the token, hex-encoded. The token itself is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When the token stops being accepted. Always UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the token was spent, or null while it is unused. A second use is refused on
    /// this column, so a replayed link cannot verify twice.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>
    /// The SHA-256 digest of the six-character code, hex-encoded — SPEC-052. The code itself is never
    /// stored, exactly as the token is not.
    /// </summary>
    /// <remarks>
    /// A second credential on the same row rather than a second row: the link and the code prove the same
    /// thing about the same address, and spending either must spend both. Two rows would be two windows,
    /// two expiries and one of them eventually outliving the other.
    /// </remarks>
    public required string CodeHash { get; set; }

    /// <summary>
    /// How many wrong codes have been offered against this row — SPEC-052 decision 3.
    /// </summary>
    /// <remarks>
    /// The budget is what makes a six-character code safe: five wrong attempts spend the row, so an
    /// attacker gets five guesses out of a billion and then has to cause a new mail through an endpoint
    /// that is rate-limited per IP and per account. Without this column the code would be a liability.
    /// </remarks>
    public int Attempts { get; set; }
}
