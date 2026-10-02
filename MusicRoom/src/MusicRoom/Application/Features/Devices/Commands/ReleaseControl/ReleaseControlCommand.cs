using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Devices.Commands.ReleaseControl;

/// <summary>
/// Returns a device to its owner's control — `DELETE /api/v1/devices/{deviceId}/control`.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <remarks>
/// Two legitimate intents, one effect: the owner revoking and the controller handing it back. One route and one
/// statement, because a second route would be a second way to write the same state — SPEC-020 decision 6.
/// </remarks>
public sealed record ReleaseControlCommand(Guid DeviceId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "device.release-control";

    /// <inheritdoc />
    public string AuditTargetType => "device";
}
