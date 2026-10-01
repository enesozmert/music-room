using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Devices.Commands.RegisterDevice;

/// <summary>
/// Registers the calling device — `POST /api/v1/devices`.
/// </summary>
/// <remarks>
/// It carries nothing. The platform, the model and the application version come from the mandatory client
/// headers the request already had to send (SDD §10.7), read through <c>IClientContextAccessor</c>: a body that
/// repeated them would be a second source that can disagree with the audit record written for the same request —
/// SPEC-020 decision 7.
/// </remarks>
public sealed record RegisterDeviceCommand : IRequest<DeviceRegistration>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "device.register";

    /// <inheritdoc />
    public string AuditTargetType => "device";
}

/// <summary>What registering answered.</summary>
/// <param name="Outcome">Written, or a conflict because this account already has this device.</param>
/// <param name="DeviceId">The device, when one was written.</param>
public sealed record DeviceRegistration(DeviceWriteOutcome Outcome, Guid? DeviceId);
