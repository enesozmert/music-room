using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Friends.Commands.RespondToFriendRequest;

/// <summary>
/// Accepts or declines a request addressed to the caller — `PATCH /api/v1/me/friend-requests/{requestId}`.
/// </summary>
/// <param name="RequestId">The request to answer.</param>
/// <param name="Accept">True to accept, false to decline.</param>
/// <remarks>
/// One command for both answers, because they are one conditional <c>UPDATE</c> apart and the authorisation
/// question — "is this request mine to answer" — is identical.
/// </remarks>
public sealed record RespondToFriendRequestCommand(Guid RequestId, bool Accept)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => Accept ? "friends.accept" : "friends.decline";

    /// <inheritdoc />
    public string AuditTargetType => "friend-request";
}
