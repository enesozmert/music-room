using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Commands.ReleaseControl;

/// <summary>
/// Returns control to the owner, for the owner or the current controller.
/// </summary>
internal sealed class ReleaseControlCommandHandler : IRequestHandler<ReleaseControlCommand, bool>
{
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="currentUser">Who is releasing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">Who is told that control moved, once this has committed — ADR-0038.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ReleaseControlCommandHandler(
        IDeviceRepository devices,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _devices = devices;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Releases it, or reports that there was nothing this caller could release.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was updated.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(ReleaseControlCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ReleaseControlCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        _audit.SetTargetId(request.DeviceId);

        // Read first, because the row is about to stop saying who held control and that is who must be told.
        // `FindControlAsync` answers only for the owner or the controller, so this read is also the gate the
        // write repeats.
        DeviceControl? before = await _devices
            .FindControlAsync(request.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        // No `If-Match` here on purpose: returning a device to its owner is idempotent and always correct, and
        // a precondition would let a stale token keep a controller in place longer than the owner wanted.
        bool released = await _devices
            .TryReleaseControlAsync(request.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!released || before is null)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return false;
        }

        Announce(before);

        return true;
    }

    /// <summary>
    /// Tells the controller who lost it and the owner who has it back — SPEC-022 decision 2.
    /// </summary>
    /// <remarks>
    /// One event for both intents: the owner's revoke and the controller's release have the same effect, and a
    /// listener needs the state rather than which button produced it.
    /// </remarks>
    private void Announce(DeviceControl before)
    {
        DateTime nowUtc = DateTime.UtcNow;

        _broadcasts.Enqueue(new ControlRevoked(
            before.ControllerUserId,
            before.DeviceId,
            nowUtc,
            before.OwnerId));

        if (before.ControllerUserId != before.OwnerId)
        {
            _broadcasts.Enqueue(new ControlRevoked(
                before.OwnerId,
                before.DeviceId,
                nowUtc,
                before.OwnerId));
        }
    }
}
