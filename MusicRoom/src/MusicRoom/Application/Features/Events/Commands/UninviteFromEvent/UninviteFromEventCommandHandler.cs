using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.UninviteFromEvent;

/// <summary>
/// Removes the invitation row, if the caller owns the event.
/// </summary>
internal sealed class UninviteFromEventCommandHandler : IRequestHandler<UninviteFromEventCommand, bool>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is withdrawing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public UninviteFromEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Withdraws it, or reports that there was nothing to withdraw.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(
        UninviteFromEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "UninviteFromEventCommand reached its handler with no authenticated caller. The route "
                + "requires authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        bool removed = await _events
            .TryUninviteAsync(request.EventId, ownerId, request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (!removed)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return removed;
    }
}
