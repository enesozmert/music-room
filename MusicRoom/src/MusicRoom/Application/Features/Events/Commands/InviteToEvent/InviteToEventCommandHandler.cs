using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.InviteToEvent;

/// <summary>
/// Writes the invitation row, if the caller owns the event.
/// </summary>
internal sealed class InviteToEventCommandHandler
    : IRequestHandler<InviteToEventCommand, RosterWriteOutcome>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is inviting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public InviteToEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Invites them, or reports the conflict.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict, or no such event for this owner.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<RosterWriteOutcome> Handle(
        InviteToEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "InviteToEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        RosterWriteOutcome outcome = await _events
            .TryInviteAsync(request.EventId, ownerId, request.UserId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (outcome != RosterWriteOutcome.Added)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }
}
