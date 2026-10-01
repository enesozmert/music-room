using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.SetEventLicence;

/// <summary>
/// Replaces the licence, if the caller owns the event.
/// </summary>
internal sealed class SetEventLicenceCommandHandler : IRequestHandler<SetEventLicenceCommand, bool>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is setting it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SetEventLicenceCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Sets it, or reports that there is no such event for this owner.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was updated.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(SetEventLicenceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "SetEventLicenceCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        bool updated = await _events
            .TrySetLicenceAsync(request.EventId, ownerId, request.Licence, cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return updated;
    }
}
