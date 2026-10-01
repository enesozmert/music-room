using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Queries.GetDeviceControl;

/// <summary>
/// Reads the control state for a caller allowed to ask.
/// </summary>
internal sealed class GetDeviceControlQueryHandler : IRequestHandler<GetDeviceControlQuery, DeviceControl?>
{
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetDeviceControlQueryHandler(IDeviceRepository devices, ICurrentUserAccessor currentUser)
    {
        _devices = devices;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null when the caller may not ask.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The control state and its version, or null.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<DeviceControl?> Handle(
        GetDeviceControlQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetDeviceControlQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        return await _devices
            .FindControlAsync(request.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
