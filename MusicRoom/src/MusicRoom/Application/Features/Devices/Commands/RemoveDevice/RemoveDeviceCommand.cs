using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Devices.Commands.RemoveDevice;

/// <summary>
/// Removes one of the caller's own devices — `DELETE /api/v1/devices/{deviceId}`.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <remarks>
/// A hard delete: a device row carries no history worth keeping, and the audit records that name it hold its id
/// without a foreign key, so they survive it — SPEC-008's deliberate omission.
/// </remarks>
public sealed record RemoveDeviceCommand(Guid DeviceId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "device.remove";

    /// <inheritdoc />
    public string AuditTargetType => "device";
}
