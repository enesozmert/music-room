using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One opaque refresh token: the right to exchange it, exactly once, for a new token pair —
/// SDD §13.1.
/// </summary>
/// <remarks>
/// The row holds the <em>hash</em> of the token, never the token, for the same reason
/// <see cref="EmailVerificationToken"/> does: a leaked database hands nobody a working session.
/// <para>
/// Every token descended from one login shares a <see cref="FamilyId"/>. Reuse of an already-spent
/// token means the value leaked, so the whole family is revoked — and the family column makes that
/// one indexed <c>UPDATE</c> instead of a walk along a chain of parent pointers.
/// </para>
/// </remarks>
public sealed class RefreshToken : Entity
{
    /// <summary>The account this token signs in.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The login this token descends from. Set once when the family is created and copied to every
    /// replacement, so revoking a family needs no recursion.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>The SHA-256 digest of the token, hex-encoded. The token itself is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// When the token stops being accepted. Always UTC. Inherited from the family's first token, so
    /// rotation cannot extend a session beyond the 30 days the owner decided on 2026-09-27.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the token was exchanged, or null while it is unused. A second exchange is
    /// refused on this column, so a replayed token cannot mint a second session.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>When the token was revoked, or null while it is live. Reuse of a spent sibling
    /// stamps every unrevoked row in the family, which is the theft response SDD §13.1 requires.</summary>
    public DateTime? RevokedAtUtc { get; set; }
}
