using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Queries.ListEvents;

/// <summary>
/// Reads a page of events in one scope.
/// </summary>
internal sealed class ListEventsQueryHandler
    : IRequestHandler<ListEventsQuery, IReadOnlyList<EventSummary>>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is listing, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListEventsQueryHandler(IEventRepository events, ICurrentUserAccessor currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    /// <summary>Lists them.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<IReadOnlyList<EventSummary>> Handle(
        ListEventsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListEventsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        return await _events
            .ListAsync(userId, request.Scope, request.Page, request.PageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
