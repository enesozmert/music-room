using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Friends.Commands.CancelFriendRequest;

/// <summary>
/// Withdraws a request the caller sent — `DELETE /api/v1/me/friend-requests/{requestId}`.
/// </summary>
/// <param name="RequestId">The request to withdraw.</param>
public sealed record CancelFriendRequestCommand(Guid RequestId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "friends.cancel-request";

    /// <inheritdoc />
    public string AuditTargetType => "friend-request";
}
