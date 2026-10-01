using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.RemoveSuggestion;

/// <summary>
/// Removes the suggestion, if the caller suggested it or owns the event.
/// </summary>
internal sealed class RemoveSuggestionCommandHandler : IRequestHandler<RemoveSuggestionCommand, bool>
{
    private readonly IEventQueueRepository _queue;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the queue, the caller and the audit context.</summary>
    /// <param name="queue">Where suggestions live.</param>
    /// <param name="currentUser">Who is removing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RemoveSuggestionCommandHandler(
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

    /// <summary>Removes it, or reports that there was nothing this caller could remove.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(RemoveSuggestionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RemoveSuggestionCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        // The two permitted callers are both expressed in the delete's own predicate, so there is no
        // read-then-write window and no second answer to keep in step.
        bool removed = await _queue
            .TryRemoveSuggestionAsync(request.EventId, request.SuggestionId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!removed)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return removed;
        }

        // The queue's membership changed, so its order did. The votes went with the row, which is why no
        // `VoteChanged` follows.
        _broadcasts.Enqueue(
            new QueueReordered(request.EventId, DateTime.UtcNow, request.SuggestionId));

        return removed;
    }
}
