using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.SetEventLicence;

/// <summary>
/// Replaces an event's visibility, licence and presence policy — `PUT /api/v1/events/{eventId}/licence`.
/// </summary>
/// <param name="EventId">The event.</param>
/// <param name="Licence">The visibility, the mode, and ADR-0014's policy.</param>
/// <remarks>
/// Owner only: who may vote, and who may even find the event, is not a decision a participant makes.
/// </remarks>
public sealed record SetEventLicenceCommand(Guid EventId, EventLicence Licence)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.set-licence";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
