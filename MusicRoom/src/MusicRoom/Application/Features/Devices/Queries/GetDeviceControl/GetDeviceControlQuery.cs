using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Devices.Queries.GetDeviceControl;

/// <summary>
/// Who controls a device — `GET /api/v1/devices/{deviceId}/control`.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <remarks>
/// The owner or the current controller may ask; anybody else gets null, which the endpoint turns into `404`.
/// The response carries the `ETag` a claim must send back as `If-Match`.
/// </remarks>
public sealed record GetDeviceControlQuery(Guid DeviceId) : IRequest<DeviceControl?>;
