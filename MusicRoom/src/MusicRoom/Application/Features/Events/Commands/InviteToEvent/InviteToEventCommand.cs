using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.InviteToEvent;

/// <summary>
/// Invites somebody to an event — `POST /api/v1/events/{eventId}/invitations`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="UserId">Who to invite.</param>
/// <remarks>
/// Owner only. On a private event the invitation **is** the `member` gate, so this is the route that decides
/// who can see the event at all.
/// </remarks>
public sealed record InviteToEventCommand(Guid EventId, Guid UserId)
    : IRequest<RosterWriteOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.invite";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
