using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Devices.Queries.ListControlGrants;

/// <summary>
/// The devices the caller has been given control of — `GET /api/v1/me/control-grants`.
/// </summary>
/// <remarks>
/// The `self` gate, from the other side of the delegation: what somebody else handed me, not what I handed out.
/// </remarks>
public sealed record ListControlGrantsQuery : IRequest<IReadOnlyList<ControlGrantSummary>>;
