using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Devices.Queries.ListDevices;

/// <summary>
/// Lists the caller's own devices — `GET /api/v1/devices`.
/// </summary>
/// <remarks>
/// The `self` gate, and it takes no account id: a route that accepted one would be a route somebody would
/// eventually pass somebody else's.
/// </remarks>
public sealed record ListDevicesQuery : IRequest<IReadOnlyList<DeviceSummary>>;
