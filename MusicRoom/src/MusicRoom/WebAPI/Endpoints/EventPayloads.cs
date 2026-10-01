using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>The event creation payload — <c>docs/api/ENDPOINTS.md</c> §4.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="StartsAtUtc">When it begins.</param>
/// <param name="EndsAtUtc">When it ends.</param>
/// <param name="Visibility">Who may find it. Defaults to private.</param>
/// <param name="Licence">Who may vote in it. Defaults to invited only.</param>
/// <param name="PresenceProof">Which proof a voter must supply — required by the restricted licence.</param>
/// <param name="Geofence">The venue, required when the proof involves one.</param>
/// <remarks>
/// Its own file with the other payloads, so <c>EventEndpoints</c> stays inside the file-length gate.
/// </remarks>
internal sealed record EventCreation(
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Visibility? Visibility,
    LicenceMode? Licence,
    PresenceProof? PresenceProof,
    GeofenceRequest? Geofence)
{
    /// <summary>The name and the window, as the command carries them.</summary>
    internal EventMetadata ToMetadata() => new(Name, StartsAtUtc, EndsAtUtc);

    /// <summary>The licence, as the command carries it.</summary>
    /// <remarks>
    /// The defaults are the closed ones: a request that says nothing about who may find or write to an event
    /// gets a private, invited-only event rather than a public one.
    /// </remarks>
    internal EventLicence ToLicence() => new(
        Visibility ?? Domain.Common.Visibility.Private,
        Licence ?? LicenceMode.InvitedOnly,
        PresenceProof,
        Geofence?.ToPolicy());
}

/// <summary>The metadata payload for the guarded `PATCH`.</summary>
/// <param name="Name">The new name.</param>
/// <param name="StartsAtUtc">The new start.</param>
/// <param name="EndsAtUtc">The new end.</param>
internal sealed record EventMetadataRequest(string Name, DateTime StartsAtUtc, DateTime EndsAtUtc);

/// <summary>The licence payload.</summary>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may vote in it.</param>
/// <param name="PresenceProof">Which proof a voter must supply.</param>
/// <param name="Geofence">The venue.</param>
internal sealed record EventLicenceRequest(
    Visibility Visibility,
    LicenceMode Licence,
    PresenceProof? PresenceProof,
    GeofenceRequest? Geofence)
{
    /// <summary>The licence, as the command carries it.</summary>
    internal EventLicence ToLicence() =>
        new(Visibility, Licence, PresenceProof, Geofence?.ToPolicy());
}

/// <summary>A venue and how close counts as present — ADR-0014.</summary>
/// <param name="Latitude">The venue's latitude.</param>
/// <param name="Longitude">The venue's longitude.</param>
/// <param name="RadiusMetres">How far from the centre still counts.</param>
internal sealed record GeofenceRequest(decimal Latitude, decimal Longitude, int RadiusMetres)
{
    /// <summary>The policy, as the command carries it.</summary>
    internal PresencePolicy ToPolicy() => new(Latitude, Longitude, RadiusMetres);
}

/// <summary>One event, as a client reads it.</summary>
/// <param name="EventId">The event.</param>
/// <param name="Name">Its name.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may vote in it.</param>
/// <param name="StartsAtUtc">When it begins.</param>
/// <param name="EndsAtUtc">When it ends.</param>
/// <param name="OwnerId">Its owner.</param>
/// <param name="Geofence">The venue, when the licence has one.</param>
internal sealed record EventResponse(
    Guid EventId,
    string Name,
    Visibility Visibility,
    LicenceMode Licence,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid OwnerId,
    GeofenceRequest? Geofence)
{
    /// <summary>Maps what the query answered onto the wire shape.</summary>
    /// <param name="detail">The event.</param>
    /// <returns>The response body.</returns>
    internal static EventResponse From(EventDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new EventResponse(
            detail.EventId,
            detail.Name,
            detail.Visibility,
            detail.Licence,
            detail.StartsAtUtc,
            detail.EndsAtUtc,
            detail.OwnerId,
            detail.Policy is null
                ? null
                : new GeofenceRequest(
                    detail.Policy.Latitude,
                    detail.Policy.Longitude,
                    detail.Policy.RadiusMetres));
    }
}
