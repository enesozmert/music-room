using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Queries.ListDevices;

/// <summary>
/// Reads the caller's devices.
/// </summary>
internal sealed class ListDevicesQueryHandler
    : IRequestHandler<ListDevicesQuery, IReadOnlyList<DeviceSummary>>
{
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="currentUser">Whose devices, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListDevicesQueryHandler(IDeviceRepository devices, ICurrentUserAccessor currentUser)
    {
        _devices = devices;
        _currentUser = currentUser;
    }

    /// <summary>Lists them.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The caller's devices.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<IReadOnlyList<DeviceSummary>> Handle(
        ListDevicesQuery request,
        CancellationToken cancellationToken)
    {
        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListDevicesQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        return await _devices.ListAsync(ownerId, cancellationToken).ConfigureAwait(false);
    }
}
