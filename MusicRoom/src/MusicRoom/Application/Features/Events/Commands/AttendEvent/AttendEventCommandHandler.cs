using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.AttendEvent;

/// <summary>
/// Writes the attendance row, if the caller may see the event.
/// </summary>
/// <remarks>
/// The `member` gate, not the `licence` one: attending is not voting. On a public event that means any
/// authenticated caller; on a private one it means an invitee.
/// </remarks>
internal sealed class AttendEventCommandHandler
    : IRequestHandler<AttendEventCommand, RosterWriteOutcome>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is attending, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AttendEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Attends, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict, or no such event this caller may see.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<RosterWriteOutcome> Handle(
        AttendEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "AttendEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return RosterWriteOutcome.NotFound;
        }

        RosterWriteOutcome outcome = await _events
            .TryAttendAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (outcome != RosterWriteOutcome.Added)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }
}
