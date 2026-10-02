using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.WithdrawVote;

/// <summary>
/// Removes the caller's own vote.
/// </summary>
internal sealed class WithdrawVoteCommandHandler : IRequestHandler<WithdrawVoteCommand, bool>
{
    private readonly IEventQueueRepository _queue;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the queue, the caller and the audit context.</summary>
    /// <param name="queue">Where votes live.</param>
    /// <param name="currentUser">Whose vote it is, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public WithdrawVoteCommandHandler(
        IEventQueueRepository queue,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _queue = queue;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Withdraws it, or reports that there was no vote to withdraw.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(WithdrawVoteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "WithdrawVoteCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        VoteWriteResult result = await _queue
            .TryWithdrawVoteAsync(request.EventId, request.SuggestionId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == VoteWriteOutcome.NotFound)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return false;
        }

        // The count goes down as honestly as it goes up, and the payload carries the value rather than a
        // delta — ADR-0036.
        _broadcasts.Enqueue(
            new VoteChanged(request.EventId, DateTime.UtcNow, request.SuggestionId, result.Votes));

        return true;
    }
}
