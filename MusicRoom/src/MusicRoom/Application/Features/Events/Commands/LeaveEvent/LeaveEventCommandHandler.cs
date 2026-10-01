using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.LeaveEvent;

/// <summary>
/// Removes the caller's own attendance row.
/// </summary>
internal sealed class LeaveEventCommandHandler : IRequestHandler<LeaveEventCommand, bool>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is leaving, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public LeaveEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Leaves, or reports that there was nothing to leave.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(LeaveEventCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "LeaveEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        bool left = await _events
            .TryLeaveAsync(request.EventId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!left)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return left;
    }
}
