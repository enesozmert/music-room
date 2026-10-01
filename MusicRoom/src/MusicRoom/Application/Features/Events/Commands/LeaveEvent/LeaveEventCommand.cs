using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.LeaveEvent;

/// <summary>
/// Takes the caller off the roster — `DELETE /api/v1/events/{eventId}/attendances/current`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// `current` rather than a user id: the only attendance a caller may remove is their own, and a route that
/// accepted an id would be a route somebody would eventually pass somebody else's.
/// </remarks>
public sealed record LeaveEventCommand(Guid EventId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.leave";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
