using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.AttendEvent;

/// <summary>
/// Records that the caller is attending — `POST /api/v1/events/{eventId}/attendances`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <remarks>
/// A roster entry, **not** a gate. Subject V.2.1 says that when an event is public, all users can find it and
/// vote; nothing in this project requires an attendance row before anything else — SPEC-017 decision 5.
/// </remarks>
public sealed record AttendEventCommand(Guid EventId)
    : IRequest<RosterWriteOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.attend";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
