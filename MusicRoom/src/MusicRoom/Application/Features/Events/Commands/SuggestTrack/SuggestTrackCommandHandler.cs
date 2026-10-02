using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.SuggestTrack;

/// <summary>
/// Adds the suggestion, if the licence admits the caller.
/// </summary>
/// <remarks>
/// The `licence` gate as the row decides it. A `LocationAndTimeRestricted` event additionally requires presence
/// **to vote**; suggesting inside the window is enough, because a suggestion is not a vote and the contract
/// gives the vote the `presence` payload.
/// </remarks>
internal sealed class SuggestTrackCommandHandler
    : IRequestHandler<SuggestTrackCommand, SuggestionWriteResult>
{
    private readonly IEventRepository _events;
    private readonly IEventQueueRepository _queue;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the gate, the queue, the caller and the audit context.</summary>
    /// <param name="events">Where the `licence` gate is decided.</param>
    /// <param name="queue">Where suggestions live.</param>
    /// <param name="currentUser">Who is suggesting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SuggestTrackCommandHandler(
        IEventRepository events,
        IEventQueueRepository queue,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _events = events;
        _queue = queue;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Suggests it, or reports why not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, or null when the licence does not admit the caller.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<SuggestionWriteResult> Handle(
        SuggestTrackCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "SuggestTrackCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        DateTime nowUtc = DateTime.UtcNow;

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MayVote)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            // The same `404` a stranger gets: a distinct answer would confirm the event exists.
            return SuggestionWriteResult.NotPermitted;
        }

        SuggestionWriteResult result = await _queue
            .TrySuggestAsync(request.EventId, request.YoutubeVideoId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome != SuggestionWriteOutcome.Added)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return result;
        }

        _broadcasts.Enqueue(new TrackSuggested(
            request.EventId,
            nowUtc,
            result.SuggestionId!.Value,
            request.YoutubeVideoId));

        return result;
    }
}
