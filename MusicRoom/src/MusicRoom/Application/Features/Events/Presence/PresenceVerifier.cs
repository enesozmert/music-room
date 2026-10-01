using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Events.Presence;

/// <summary>
/// Rules on the evidence a voter supplied — ADR-0014, SPEC-018 decisions 5 and 6.
/// </summary>
/// <remarks>
/// The client never decides whether it may vote; it supplies evidence and this type rules on it, with server
/// time and the event's own stored policy.
/// <para>
/// A submitted coordinate is compared and dropped. It is not written to the vote row, not returned, and not
/// logged — storing every voter's position would be a privacy liability with no requirement behind it, and a log
/// line is a row with worse retention (`application-security` data minimisation).
/// </para>
/// </remarks>
public sealed class PresenceVerifier
{
    private readonly IEventCodeRepository _codes;

    /// <summary>Takes the code store.</summary>
    /// <param name="codes">Where a code is verified and spent.</param>
    public PresenceVerifier(IEventCodeRepository codes) => _codes = codes;

    /// <summary>Whether this caller has proved presence for this event.</summary>
    /// <param name="subject">The event's policy and window, as the row holds them.</param>
    /// <param name="proof">What the client supplied, which may be nothing.</param>
    /// <param name="userId">The caller, from the validated token.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the licence admits this caller right now.</returns>
    /// <remarks>
    /// An event whose licence is not <see cref="LicenceMode.LocationAndTimeRestricted"/> needs no proof and is
    /// not asked for one: a presence check on a licence that does not require presence would be theatre.
    /// </remarks>
    public async Task<bool> IsPresentAsync(
        PresenceSubject subject,
        PresenceProofSubmission? proof,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (subject.Licence != LicenceMode.LocationAndTimeRestricted)
        {
            return true;
        }

        // Server time, never the client's — a client clock is evidence of nothing (ADR-0014).
        if (!subject.InWindow)
        {
            return false;
        }

        return subject.Proof switch
        {
            PresenceProof.Geofence => InsideGeofence(subject, proof),
            PresenceProof.EventCode =>
                await SpentCodeAsync(subject, proof, userId, cancellationToken).ConfigureAwait(false),
            PresenceProof.Both =>
                InsideGeofence(subject, proof)
                && await SpentCodeAsync(subject, proof, userId, cancellationToken).ConfigureAwait(false),

            // A restricted licence with no configured proof cannot be created or set — SPEC-017 decision 3 —
            // so reaching here means the row was written by something other than this application. Refuse.
            _ => false,
        };
    }

    private static bool InsideGeofence(PresenceSubject subject, PresenceProofSubmission? proof)
    {
        if (subject.Geofence is not Geofence geofence
            || proof?.Latitude is not double latitude
            || proof.Longitude is not double longitude)
        {
            return false;
        }

        return Geodesy.IsWithin(geofence, latitude, longitude);
    }

    private async Task<bool> SpentCodeAsync(
        PresenceSubject subject,
        PresenceProofSubmission? proof,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proof?.EventCode))
        {
            return false;
        }

        // Hashed the way every other opaque token in this application is, and compared as a digest: the
        // plaintext exists in the request and nowhere else.
        return await _codes
            .TrySpendAsync(
                subject.EventId,
                OpaqueTokenHash.Of(proof.EventCode.Trim()),
                userId,
                subject.NowUtc,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>What the event requires, as its row holds it.</summary>
/// <param name="EventId">The event.</param>
/// <param name="Licence">Its licence mode.</param>
/// <param name="Proof">Which proof it requires, or null when it requires none.</param>
/// <param name="Geofence">Its venue, or null.</param>
/// <param name="InWindow">Whether server time is inside its window.</param>
/// <param name="NowUtc">Server time, passed in so the verifier reads no clock of its own.</param>
public sealed record PresenceSubject(
    Guid EventId,
    LicenceMode Licence,
    PresenceProof? Proof,
    Geofence? Geofence,
    bool InWindow,
    DateTime NowUtc);

/// <summary>
/// What the client supplied with its vote — `presence` in <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <param name="Latitude">The claimed latitude, in degrees.</param>
/// <param name="Longitude">The claimed longitude, in degrees.</param>
/// <param name="EventCode">The code read at the venue.</param>
/// <remarks>
/// Every member is optional because an unrestricted event needs none of them, and because a request that omits
/// what the licence requires must be refused rather than rejected by the binder: `422` naming the licence is a
/// better answer than `400` naming a field.
/// </remarks>
public sealed record PresenceProofSubmission(double? Latitude, double? Longitude, string? EventCode);
