using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.UpdateEvent;

/// <summary>
/// Changes an event's metadata — `PATCH /api/v1/events/{eventId}`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="Metadata">The new name and window.</param>
/// <param name="ExpectedVersion">The `ETag` the caller last read, from `If-Match`.</param>
public sealed record UpdateEventCommand(Guid EventId, EventMetadata Metadata, uint ExpectedVersion)
    : IRequest<EventWriteOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.update";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
