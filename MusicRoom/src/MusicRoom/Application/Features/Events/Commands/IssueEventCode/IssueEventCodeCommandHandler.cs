using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Events.Presence;

namespace MusicRoom.Application.Features.Events.Commands.IssueEventCode;

/// <summary>
/// Mints a code for the owner, stores its digest, and returns the plaintext once.
/// </summary>
/// <remarks>
/// The only moment the plaintext exists outside the owner's screen is this response. The row holds a SHA-256
/// digest, so a leaked database does not let anybody vote — the same posture the refresh and reset tokens have.
/// Nothing here is logged.
/// </remarks>
internal sealed class IssueEventCodeCommandHandler : IRequestHandler<IssueEventCodeCommand, string?>
{
    private readonly IEventRepository _events;
    private readonly IEventCodeRepository _codes;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the gate, the code store, the caller and the audit context.</summary>
    /// <param name="events">Where the `owner` gate is decided.</param>
    /// <param name="codes">Where the digest is written.</param>
    /// <param name="currentUser">Who is issuing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public IssueEventCodeCommandHandler(
        IEventRepository events,
        IEventCodeRepository codes,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _events = events;
        _codes = codes;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Issues it, or answers null when the caller does not own the event.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The plaintext code, or null.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<string?> Handle(IssueEventCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "IssueEventCodeCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-018.");

        _audit.SetTargetId(request.EventId);

        DateTime nowUtc = DateTime.UtcNow;

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, ownerId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!access.IsOwner)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return null;
        }

        string code = EventCodeGenerator.Create();

        await _codes
            .IssueAsync(
                request.EventId,
                OpaqueTokenHash.Of(code),
                nowUtc.AddSeconds(request.TimeToLiveSeconds),
                ownerId,
                cancellationToken)
            .ConfigureAwait(false);

        return code;
    }
}
