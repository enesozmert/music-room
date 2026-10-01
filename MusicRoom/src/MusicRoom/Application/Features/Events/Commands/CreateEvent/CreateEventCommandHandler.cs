using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Events.Commands.CreateEvent;

/// <summary>
/// Writes the event, owned by the caller.
/// </summary>
internal sealed class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, Guid>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is creating it, from the validated token.</param>
    /// <param name="audit">Where this handler names what it created — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public CreateEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Creates it and reports its id.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The new event's id.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<Guid> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "CreateEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        Event newEvent = new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = request.Metadata.Name.Trim(),
            Visibility = request.Licence.Visibility,
            Licence = request.Licence.Licence,
            StartsAtUtc = request.Metadata.StartsAtUtc,
            EndsAtUtc = request.Metadata.EndsAtUtc,
            PresenceProof = request.Licence.Proof,
            GeofenceLatitude = request.Licence.Geofence?.Latitude,
            GeofenceLongitude = request.Licence.Geofence?.Longitude,
            GeofenceRadiusMetres = request.Licence.Geofence?.RadiusMetres,
        };

        await _events.CreateAsync(newEvent, cancellationToken).ConfigureAwait(false);

        _audit.SetTargetId(newEvent.Id);

        return newEvent.Id;
    }
}
