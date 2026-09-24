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
}
