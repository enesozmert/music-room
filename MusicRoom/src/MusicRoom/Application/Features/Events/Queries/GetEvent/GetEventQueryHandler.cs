using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Queries.GetEvent;

/// <summary>
/// Reads an event for a caller who may see it — the `member` gate.
/// </summary>
internal sealed class GetEventQueryHandler : IRequestHandler<GetEventQuery, EventDetail?>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetEventQueryHandler(IEventRepository events, ICurrentUserAccessor currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The event, or null when the caller may not see it.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<EventDetail?> Handle(GetEventQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetEventQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        return await _events
            .FindAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }
}
