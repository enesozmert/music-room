using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Events.Presence;

namespace MusicRoom.Application.Features.Events.Commands.CastVote;

/// <summary>
/// Records a vote, if the licence admits the caller and they can prove presence — ADR-0014.
/// </summary>
/// <remarks>
/// Two gates in order, and both are needed: <see cref="EventAccess.MayVote"/> answers what the row decides —
/// visibility, licence mode, and whether server time is inside the window — and the verifier answers what the
/// client must show. Neither alone is the `licence` gate of ENDPOINTS §4.
/// <para>
/// Nothing about the submitted coordinate survives this method. It is compared and dropped: not stored on the
/// vote, not returned, not logged (SPEC-018 decision 5).
/// </para>
/// </remarks>
internal sealed class CastVoteCommandHandler : IRequestHandler<CastVoteCommand, VoteWriteOutcome>
{
    private readonly IEventRepository _events;
    private readonly IEventQueueRepository _queue;
    private readonly PresenceVerifier _presence;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the gate, the queue, the verifier, the caller and the audit context.</summary>
    /// <param name="events">Where the `licence` gate and the policy are read.</param>
    /// <param name="queue">Where votes live.</param>
    /// <param name="presence">What rules on the evidence — ADR-0014.</param>
    /// <param name="currentUser">Who is voting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public CastVoteCommandHandler(
        IEventRepository events,
        IEventQueueRepository queue,
        PresenceVerifier presence,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _queue = queue;
        _presence = presence;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Votes, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Recorded, unchanged, not found, or refused for want of presence.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<VoteWriteOutcome> Handle(
        CastVoteCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "CastVoteCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        DateTime nowUtc = DateTime.UtcNow;

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MayVote)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return VoteWriteOutcome.NotFound;
        }

        if (!await ProvedPresenceAsync(request, userId, nowUtc, cancellationToken).ConfigureAwait(false))
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return VoteWriteOutcome.PresenceNotProved;
        }

        VoteWriteOutcome outcome = await _queue
            .TryCastVoteAsync(request.EventId, request.SuggestionId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (outcome == VoteWriteOutcome.NotFound)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }

    /// <summary>Asks the verifier, with the event's own stored policy.</summary>
    private async Task<bool> ProvedPresenceAsync(
        CastVoteCommand request,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        EventPresence? presence = await _events
            .FindPresenceAsync(request.EventId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (presence is null)
        {
            return false;
        }

        PresenceSubject subject = new(
            request.EventId,
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

        return await _presence
            .IsPresentAsync(subject, request.Proof, userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
