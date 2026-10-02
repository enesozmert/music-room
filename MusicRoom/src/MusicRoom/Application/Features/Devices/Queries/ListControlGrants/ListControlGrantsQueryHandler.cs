using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Queries.ListControlGrants;

/// <summary>
/// Reads the grants the caller holds.
/// </summary>
internal sealed class ListControlGrantsQueryHandler
    : IRequestHandler<ListControlGrantsQuery, IReadOnlyList<ControlGrantSummary>>
{
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="currentUser">Who holds the grants, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListControlGrantsQueryHandler(IDeviceRepository devices, ICurrentUserAccessor currentUser)
    {
        _devices = devices;
        _currentUser = currentUser;
    }

    /// <summary>Lists them.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The grants the caller holds.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<IReadOnlyList<ControlGrantSummary>> Handle(
        ListControlGrantsQuery request,
        CancellationToken cancellationToken)
    {
        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListControlGrantsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        return await _devices.ListGrantsAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}
