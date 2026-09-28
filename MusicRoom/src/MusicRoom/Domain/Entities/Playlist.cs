using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A collaboratively edited playlist — subject V.2.3.
/// </summary>
/// <remarks>
/// <see cref="Version"/> is PostgreSQL's <c>xmin</c>, mapped as a concurrency token: it changes on every
/// update without the application remembering to increment anything, and it is what the `ETag` and `If-Match`
/// of `docs/api/ENDPOINTS.md` §5 carry. SDD §12.2 already named `xmin` for the reorder path, so the aggregate
/// uses one mechanism for both.
/// <para>
/// Deletion is soft — <see cref="DeletedAtUtc"/> plus a global query filter — so a forgotten `WHERE` cannot
/// resurrect a deleted playlist.
/// </para>
/// </remarks>
public sealed class Playlist : Entity
{
    /// <summary>The account that created it and may delete it.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>Who may find it.</summary>
    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>Who may write to it. A playlist never carries <c>LocationAndTimeRestricted</c>.</summary>
    public LicenceMode Licence { get; set; } = LicenceMode.InvitedOnly;

    /// <summary>When it was soft-deleted, or null while it exists.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// PostgreSQL's <c>xmin</c>, the concurrency token. Never set by this application.
    /// </summary>
    public uint Version { get; set; }
}
