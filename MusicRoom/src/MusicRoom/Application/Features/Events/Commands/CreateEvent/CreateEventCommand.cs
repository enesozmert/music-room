using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Events.Commands.CreateEvent;

/// <summary>
/// Creates an event — `POST /api/v1/events`.
/// </summary>
/// <param name="Metadata">The name and the window.</param>
/// <param name="Licence">The visibility, the mode, and ADR-0014's policy.</param>
/// <remarks>
/// Two payload records rather than seven parameters, which the five-parameter gate would refuse — and which
/// would also let a caller pass a centre without a radius. The caller is the owner, taken from the validated
/// token and never from the request.
/// </remarks>
public sealed record CreateEventCommand(EventMetadata Metadata, EventLicence Licence)
    : IRequest<Guid>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "event.create";

    /// <inheritdoc />
    public string AuditTargetType => "event";
}
