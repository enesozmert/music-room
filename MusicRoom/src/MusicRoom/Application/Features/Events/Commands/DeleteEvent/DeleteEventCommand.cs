using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Events.Commands.DeleteEvent;

/// <summary>
/// Soft-deletes an event — `DELETE /api/v1/events/{eventId}`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// The row stays, stamped, and a global query filter hides it from every read, so the audit trail keeps
/// pointing at something that exists.
/// </remarks>
public sealed record DeleteEventCommand(Guid EventId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.delete";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
