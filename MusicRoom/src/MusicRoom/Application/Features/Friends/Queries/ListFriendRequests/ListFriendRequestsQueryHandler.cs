using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;

/// <summary>
/// Reads the caller's pending requests, in the direction asked for.
/// </summary>
internal sealed class ListFriendRequestsQueryHandler
    : IRequestHandler<ListFriendRequestsQuery, IReadOnlyList<FriendRequestSummary>>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the graph and the caller.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListFriendRequestsQueryHandler(
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser)
    {
        _friendships = friendships;
        _currentUser = currentUser;
    }

    /// <summary>Lists them, newest first.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per pending request in that direction.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<IReadOnlyList<FriendRequestSummary>> Handle(
        ListFriendRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListFriendRequestsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-012.");

        return _friendships.ListRequestsAsync(
            userId,
            request.Direction,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
