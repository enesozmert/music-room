using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Devices.Commands.GrantControl;

/// <summary>
/// Hands playback control of a device to a friend — `PUT /api/v1/devices/{deviceId}/control`.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <param name="GranteeUserId">Who is being given control. Must be a friend of the owner.</param>
/// <param name="ExpectedVersion">The `ETag` the caller last read, from `If-Match`.</param>
/// <remarks>
/// Owner only, and guarded: two simultaneous claims resolve to one `200` and one `412`, because the loser of
/// this race must lose **visibly** — two clients each believing they hold one device is the failure subject
/// V.2.2 is asking about.
/// </remarks>
public sealed record GrantControlCommand(Guid DeviceId, Guid GranteeUserId, uint ExpectedVersion)
    : IRequest<DeviceWriteOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "device.grant-control";

    /// <inheritdoc />
    public string AuditTargetType => "device";
}
