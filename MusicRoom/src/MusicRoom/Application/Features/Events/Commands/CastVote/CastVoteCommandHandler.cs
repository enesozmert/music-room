using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
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
internal sealed class CastVoteCommandHandler : IRequestHandler<CastVoteCommand, VoteWriteResult>
{
    private readonly EventVoteGate _gate;
    private readonly IEventQueueRepository _queue;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the gate, the queue, the caller, the audit context and the broadcast queue.</summary>
    /// <param name="gate">The licence and the presence proof, as one question — ADR-0014.</param>
    /// <param name="queue">Where votes live.</param>
    /// <param name="currentUser">Who is voting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public CastVoteCommandHandler(
        EventVoteGate gate,
        IEventQueueRepository queue,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _gate = gate;
        _queue = queue;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Votes, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Recorded, unchanged, not found, or refused for want of presence — with the new count.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<VoteWriteResult> Handle(
        CastVoteCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "CastVoteCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        VoteGateDecision decision = await _gate
            .DecideAsync(request.EventId, userId, request.Proof, cancellationToken)
            .ConfigureAwait(false);

        if (decision != VoteGateDecision.Admitted)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return decision == VoteGateDecision.PresenceNotProved
                ? VoteWriteResult.PresenceNotProved
                : VoteWriteResult.NotFound;
        }

        DateTime nowUtc = DateTime.UtcNow;

        VoteWriteResult result = await _queue
            .TryCastVoteAsync(request.EventId, request.SuggestionId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == VoteWriteOutcome.NotFound)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return result;
        }

        // A duplicate vote changed nothing, so nobody is told: the count every listener holds is already right.
        if (result.Outcome == VoteWriteOutcome.Recorded)
        {
            _broadcasts.Enqueue(
                new VoteChanged(request.EventId, nowUtc, request.SuggestionId, result.Votes));
        }

        return result;
    }
}
