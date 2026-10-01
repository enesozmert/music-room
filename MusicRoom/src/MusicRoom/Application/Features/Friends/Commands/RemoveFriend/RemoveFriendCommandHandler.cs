using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Commands.RemoveFriend;

/// <summary>
/// Deletes the friendship row for a pair, from either side.
/// </summary>
internal sealed class RemoveFriendCommandHandler : IRequestHandler<RemoveFriendCommand, bool>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the graph, the caller and the audit context.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is removing, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RemoveFriendCommandHandler(
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _friendships = friendships;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Removes the friendship, if there is one.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a friendship between the two was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(RemoveFriendCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RemoveFriendCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-012.");

        _audit.SetTargetId(request.FriendUserId);

        bool removed = await _friendships
            .TryRemoveFriendAsync(userId, request.FriendUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!removed)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return removed;
    }
}
