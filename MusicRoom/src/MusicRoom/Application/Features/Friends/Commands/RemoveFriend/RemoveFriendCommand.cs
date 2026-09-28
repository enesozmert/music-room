using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Friends.Commands.RemoveFriend;

/// <summary>
/// Ends a friendship — `DELETE /api/v1/me/friends/{userId}`.
/// </summary>
/// <param name="FriendUserId">The friend to remove.</param>
/// <remarks>
/// One row is deleted, so it ends for both sides at once: there is no state in which one of them still has a
/// friend the other does not.
/// </remarks>
public sealed record RemoveFriendCommand(Guid FriendUserId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "friends.remove";

    /// <inheritdoc />
    public string AuditTargetType => "friendship";
}
