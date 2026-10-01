using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Commands.UpdateEvent;

/// <summary>
/// Changes the metadata, if the caller owns the event and still holds its version.
/// </summary>
internal sealed class UpdateEventCommandHandler
    : IRequestHandler<UpdateEventCommand, EventWriteOutcome>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is editing, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public UpdateEventCommandHandler(
        IEventRepository events,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Updates it, or reports why not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `200`, `412` or `404`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<EventWriteOutcome> Handle(
        UpdateEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "UpdateEventCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        _audit.SetTargetId(request.EventId);

        EventWriteOutcome outcome = await _events
            .TryUpdateAsync(
                request.EventId,
                ownerId,
                request.Metadata with { Name = request.Metadata.Name.Trim() },
                request.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome != EventWriteOutcome.Updated)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }
}
