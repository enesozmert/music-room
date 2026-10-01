using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Presence;

/// <summary>
/// The two gates a vote passes: the licence, then the proof — ENDPOINTS §4's `licence` symbol, ADR-0014.
/// </summary>
/// <remarks>
/// One type because they are one question asked in two halves, and neither half is the gate by itself:
/// <see cref="EventAccess.MayVote"/> answers what the event's row decides — visibility, licence mode, and
/// whether server time is inside the window — and <see cref="PresenceVerifier"/> answers what the client must
/// show. Keeping them together means no future caller can ask one and believe it asked both.
/// <para>
/// Extracted when the vote handler's constructor reached six parameters, which the five-parameter gate refuses
/// — the same reason <c>SessionIssuer</c> and <c>CredentialMailDispatcher</c> exist.
/// </para>
/// </remarks>
public sealed class EventVoteGate
{
    private readonly IEventRepository _events;
    private readonly PresenceVerifier _presence;

    /// <summary>Takes the event read path and the verifier.</summary>
    /// <param name="events">Where the gate and the policy are read.</param>
    /// <param name="presence">What rules on the evidence — ADR-0014.</param>
    public EventVoteGate(IEventRepository events, PresenceVerifier presence)
    {
        _events = events;
        _presence = presence;
    }

    /// <summary>Whether this caller may vote in this event right now.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller, from the validated token.</param>
    /// <param name="proof">What the client supplied, which may be nothing.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Admitted, or the reason it was refused.</returns>
    public async Task<VoteGateDecision> DecideAsync(
        Guid eventId,
        Guid userId,
        PresenceProofSubmission? proof,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        EventAccess access = await _events
            .FindAccessAsync(eventId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MayVote)
        {
            return VoteGateDecision.NotPermitted;
        }

        EventPresence? presence = await _events
            .FindPresenceAsync(eventId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (presence is null)
        {
            return VoteGateDecision.NotPermitted;
        }

        bool proved = await _presence
            .IsPresentAsync(Subject(eventId, presence, nowUtc), proof, userId, cancellationToken)
            .ConfigureAwait(false);

        return proved ? VoteGateDecision.Admitted : VoteGateDecision.PresenceNotProved;
    }

    private static PresenceSubject Subject(Guid eventId, EventPresence presence, DateTime nowUtc) => new(
        eventId,
        presence.Licence,
        presence.Proof,
        presence.Policy is null
            ? null
            : new Geofence(
                (double)presence.Policy.Latitude,
                (double)presence.Policy.Longitude,
                presence.Policy.RadiusMetres),
        presence.InWindow,
        nowUtc);
}

/// <summary>What the two gates together decided.</summary>
public enum VoteGateDecision
{
    /// <summary>No such event, or one whose licence does not admit this caller — the endpoint answers `404`.</summary>
    NotPermitted = 0,

    /// <summary>Admitted.</summary>
    Admitted = 1,

    /// <summary>
    /// The licence requires presence and the caller did not prove it — the endpoint answers `422`, with one
    /// message for every way the proof could have failed (SPEC-018 decision 6).
    /// </summary>
    PresenceNotProved = 2,
}
