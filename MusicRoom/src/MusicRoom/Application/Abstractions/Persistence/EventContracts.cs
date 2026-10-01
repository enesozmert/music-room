using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// What one caller may do with one event — the three gates of <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <param name="Exists">Whether the event exists at all. False also means every other member is false.</param>
/// <param name="MaySee">The `member` gate: the owner, an invitee, or anybody when the event is public.</param>
/// <param name="MayVote">
/// The `licence` gate as far as the stored policy decides it: the owner always, anybody who may see it when
/// the licence is `Everyone`, an invitee when it is `InvitedOnly`. For `LocationAndTimeRestricted` this is
/// true only inside the window, and the presence proof ADR-0014 requires is checked by the write itself —
/// the second unit of slice S6, which owns the only consumer.
/// </param>
/// <param name="IsOwner">The `owner` gate.</param>
/// <param name="InWindow">Whether server time is inside the event's window.</param>
public sealed record EventAccess(
    bool Exists,
    bool MaySee,
    bool MayVote,
    bool IsOwner,
    bool InWindow)
{
    /// <summary>No such event, or none this caller may see.</summary>
    public static EventAccess None { get; } = new(false, false, false, false, false);
}

/// <summary>One event, as a caller who may see it reads it.</summary>
/// <param name="EventId">The event.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may vote in it.</param>
/// <param name="StartsAtUtc">When it begins.</param>
/// <param name="EndsAtUtc">When it ends.</param>
/// <param name="OwnerId">Its owner.</param>
/// <param name="Version">The `xmin`, which the endpoint publishes as the `ETag`.</param>
/// <param name="Policy">ADR-0014's presence policy, or null when the licence requires none.</param>
public sealed record EventDetail(
    Guid EventId,
    string Name,
    Visibility Visibility,
    LicenceMode Licence,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid OwnerId,
    uint Version,
    PresencePolicy? Policy);

/// <summary>One event in a list.</summary>
/// <param name="EventId">The event.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="StartsAtUtc">When it begins.</param>
/// <param name="EndsAtUtc">When it ends.</param>
/// <param name="OwnerId">Its owner.</param>
public sealed record EventSummary(
    Guid EventId,
    string Name,
    Visibility Visibility,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid OwnerId);

/// <summary>One invitation.</summary>
/// <param name="UserId">Who was invited.</param>
/// <param name="DisplayName">Their display name, so a client needs no second call.</param>
public sealed record InvitationSummary(Guid UserId, string DisplayName);

/// <summary>The metadata a guarded `PATCH` may change.</summary>
/// <param name="Name">The new name.</param>
/// <param name="StartsAtUtc">The new start.</param>
/// <param name="EndsAtUtc">The new end.</param>
public sealed record EventMetadata(string Name, DateTime StartsAtUtc, DateTime EndsAtUtc);

/// <summary>
/// The licence an owner sets — ADR-0014.
/// </summary>
/// <param name="Visibility">Who may find the event.</param>
/// <param name="Licence">Who may vote in it.</param>
/// <param name="Proof">Which proof a voter must supply, or null when the mode needs none.</param>
/// <param name="Geofence">The venue, or null when no geofence is required.</param>
/// <remarks>
/// A record rather than four parameters, for the same reason <c>PlaylistLicence</c> is one: the write would
/// otherwise be over the five-parameter gate. The geofence travels as a unit, so "a centre with no radius"
/// cannot be expressed.
/// </remarks>
public sealed record EventLicence(
    Visibility Visibility,
    LicenceMode Licence,
    PresenceProof? Proof,
    PresencePolicy? Geofence);

/// <summary>
/// Where the event is, and how close counts as present — ADR-0014.
/// </summary>
/// <param name="Latitude">The venue's latitude.</param>
/// <param name="Longitude">The venue's longitude.</param>
/// <param name="RadiusMetres">How far from the centre still counts.</param>
/// <remarks>
/// The **venue's** coordinates, which are the event's own published fact. A voter's coordinates are checked
/// and discarded and never stored anywhere — ADR-0014, SPEC-017 decision 6.
/// </remarks>
public sealed record PresencePolicy(decimal Latitude, decimal Longitude, int RadiusMetres);

/// <summary>Which events a list asks for.</summary>
public enum EventScope
{
    /// <summary>The caller's own.</summary>
    Mine = 0,

    /// <summary>The ones the caller was invited to.</summary>
    Invited = 1,

    /// <summary>Every public event.</summary>
    Public = 2,
}

/// <summary>What a guarded event write did.</summary>
public enum EventWriteOutcome
{
    /// <summary>No such event, or none this caller owns.</summary>
    NotFound = 0,

    /// <summary>Updated.</summary>
    Updated = 1,

    /// <summary>Somebody changed it after the caller read it — the endpoint answers `412`.</summary>
    VersionMismatch = 2,
}

/// <summary>What a roster write did.</summary>
public enum RosterWriteOutcome
{
    /// <summary>No such event, or none this caller may write to.</summary>
    NotFound = 0,

    /// <summary>Added.</summary>
    Added = 1,

    /// <summary>Already there — the unique index said so, which is what makes `409` a fact.</summary>
    Conflict = 2,
}
