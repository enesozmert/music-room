using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A track-vote event — subject V.2.1.
/// </summary>
/// <remarks>
/// <see cref="Version"/> is PostgreSQL's <c>xmin</c>, the same concurrency token the playlist aggregate uses,
/// and it is what the `ETag` and `If-Match` of `docs/api/ENDPOINTS.md` §4 carry. Deletion is soft, applied by
/// a global query filter.
/// <para>
/// The four policy members carry ADR-0014's licence configuration and are null unless
/// <see cref="Licence"/> is <see cref="LicenceMode.LocationAndTimeRestricted"/>. They are nullable rather
/// than defaulted because a row of zeros would read as "a geofence at the equator with no radius" instead of
/// "no geofence" — SPEC-017 decision 1. A `LocationAndTimeRestricted` event with no policy is refused before
/// it is written, so the combination does not occur.
/// </para>
/// </remarks>
public sealed class Event : Entity
{
    /// <summary>The account that created it, and the only one that may change or delete it.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>Who may find it.</summary>
    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>Who may vote in it.</summary>
    public LicenceMode Licence { get; set; } = LicenceMode.InvitedOnly;

    /// <summary>When it begins. Always UTC; server time decides, never a client clock — ADR-0014.</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>When it ends. Always after <see cref="StartsAtUtc"/>.</summary>
    public DateTime EndsAtUtc { get; set; }

    /// <summary>Which proof a voter must supply, or null when the licence needs none.</summary>
    public PresenceProof? PresenceProof { get; set; }

    /// <summary>The venue's latitude, or null when no geofence is required.</summary>
    public decimal? GeofenceLatitude { get; set; }

    /// <summary>The venue's longitude, or null when no geofence is required.</summary>
    public decimal? GeofenceLongitude { get; set; }

    /// <summary>How far from the centre counts as present, in metres.</summary>
    public int? GeofenceRadiusMetres { get; set; }

    /// <summary>When it was soft-deleted, or null while it exists.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>PostgreSQL's <c>xmin</c>, the concurrency token. Never set by this application.</summary>
    public uint Version { get; set; }
}
