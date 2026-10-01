using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Queries.ListFriends;

/// <summary>
/// Reads the caller's friends — SDD §13.2 gate 1, the <c>self</c> tier.
/// </summary>
internal sealed class ListFriendsQueryHandler : IRequestHandler<ListFriendsQuery, IReadOnlyList<FriendSummary>>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the graph and the caller.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListFriendsQueryHandler(IFriendshipRepository friendships, ICurrentUserAccessor currentUser)
    {
        _friendships = friendships;
        _currentUser = currentUser;
    }

    /// <summary>Lists them, newest friendship first.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per friendship, projected to the other side.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<IReadOnlyList<FriendSummary>> Handle(
        ListFriendsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListFriendsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-012.");

        return _friendships.ListFriendsAsync(userId, request.Page, request.PageSize, cancellationToken);
    }
}
