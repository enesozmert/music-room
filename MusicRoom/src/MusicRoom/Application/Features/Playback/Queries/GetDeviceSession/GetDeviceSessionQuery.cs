using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Queries.GetDeviceSession;

/// <summary>
/// What a device is playing — `GET /api/v1/devices/{deviceId}/playback-session`.
/// </summary>
/// <param name="DeviceId">The device.</param>
/// <remarks>
/// Asked by the device's owner or its current controller, which is the pair the contract names: the people who
/// could change what it plays are the people entitled to know what it is playing.
/// </remarks>
public sealed record GetDeviceSessionQuery(Guid DeviceId) : IRequest<PlaybackSessionDetail?>;
