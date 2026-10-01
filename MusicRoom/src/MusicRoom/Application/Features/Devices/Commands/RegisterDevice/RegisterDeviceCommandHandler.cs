using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Devices.Commands.RegisterDevice;

/// <summary>
/// Writes the device, owned by the caller, from the headers the request already carried.
/// </summary>
internal sealed class RegisterDeviceCommandHandler
    : IRequestHandler<RegisterDeviceCommand, DeviceRegistration>
{
    private readonly IDeviceRepository _devices;
    private readonly IClientContextAccessor _clientContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the client context, the caller and the audit context.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="clientContext">The three values subject V.6 already requires on every mutation.</param>
    /// <param name="currentUser">Who is registering it, from the validated token.</param>
    /// <param name="audit">Where this handler names what it created — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RegisterDeviceCommandHandler(
        IDeviceRepository devices,
        IClientContextAccessor clientContext,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _devices = devices;
        _clientContext = clientContext;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Registers it, or reports that this account already has it.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, and the new device's id on success.</returns>
    /// <exception cref="InvalidOperationException">
    /// No authenticated caller, or no client context — the middleware refuses a mutation without the headers,
    /// so reaching here without them means the pipeline was bypassed.
    /// </exception>
    public async Task<DeviceRegistration> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RegisterDeviceCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        ClientContext context = _clientContext.Current
            ?? throw new InvalidOperationException(
                "RegisterDeviceCommand reached its handler with no client context. The mandatory client headers "
                + "are validated by middleware — SDD §10.7 — and a device must not be registered with "
                + "substituted values.");

        Device device = new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = context.Platform,
            Model = context.Device,
            AppVersion = context.AppVersion,
        };

        DeviceWriteOutcome outcome = await _devices
            .TryRegisterAsync(device, cancellationToken)
            .ConfigureAwait(false);

        if (outcome != DeviceWriteOutcome.Written)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return new DeviceRegistration(outcome, null);
        }

        _audit.SetTargetId(device.Id);

        return new DeviceRegistration(outcome, device.Id);
    }
}
