using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Friends.Commands.SendFriendRequest;

/// <summary>
/// Asks another account to be friends — `POST /api/v1/me/friend-requests`.
/// </summary>
/// <param name="TargetUserId">The account being asked.</param>
public sealed record SendFriendRequestCommand(Guid TargetUserId)
    : IRequest<SendFriendRequestResult>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "friends.request";

    /// <inheritdoc />
    public string AuditTargetType => "friend-request";
}
