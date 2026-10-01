using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.UninviteFromEvent;

/// <summary>
/// Withdraws an invitation — `DELETE /api/v1/events/{eventId}/invitations/{userId}`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="UserId">Whose invitation to withdraw.</param>
/// <remarks>
/// The gate is read live on every request, so withdrawing an invitation takes effect on the invitee's next
/// call rather than on their next sign-in.
/// </remarks>
public sealed record UninviteFromEventCommand(Guid EventId, Guid UserId)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.uninvite";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
