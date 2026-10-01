using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Commands.RemoveDevice;

/// <summary>
/// Removes the device, if the caller owns it.
/// </summary>
internal sealed class RemoveDeviceCommandHandler : IRequestHandler<RemoveDeviceCommand, bool>
{
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the caller and the audit context.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="currentUser">Who is removing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RemoveDeviceCommandHandler(
        IDeviceRepository devices,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _devices = devices;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Removes it, or reports that there was nothing of the caller's to remove.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(RemoveDeviceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RemoveDeviceCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        _audit.SetTargetId(request.DeviceId);

        bool removed = await _devices
            .TryRemoveAsync(request.DeviceId, ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (!removed)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return removed;
    }
}
