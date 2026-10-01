using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.DeleteEvent;

/// <summary>
/// Stamps the event deleted, if the caller owns it.
/// </summary>
internal sealed class DeleteEventCommandHandler : IRequestHandler<DeleteEventCommand, bool>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is deleting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public DeleteEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Deletes it, or reports that there is no such event for this owner.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was stamped.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(DeleteEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "DeleteEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        bool deleted = await _events
            .TryDeleteAsync(request.EventId, ownerId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return deleted;
    }
}
