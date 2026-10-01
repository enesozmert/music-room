using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Queries.GetQueue;

/// <summary>
/// Reads the queue for a caller who may see the event.
/// </summary>
internal sealed class GetQueueQueryHandler : IRequestHandler<GetQueueQuery, IReadOnlyList<QueueEntry>?>
{
    private readonly IEventRepository _events;
    private readonly IEventQueueRepository _queue;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the gate, the queue and the caller.</summary>
    /// <param name="events">Where the `member` gate is decided.</param>
    /// <param name="queue">Where suggestions and votes live.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetQueueQueryHandler(
        IEventRepository events,
        IEventQueueRepository queue,
        ICurrentUserAccessor currentUser)
    {
        _events = events;
        _queue = queue;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null when the caller may not see the event.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page, or null.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<IReadOnlyList<QueueEntry>?> Handle(
        GetQueueQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetQueueQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            return null;
        }

        return await _queue
            .ListQueueAsync(request.EventId, userId, request.Page, request.PageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
