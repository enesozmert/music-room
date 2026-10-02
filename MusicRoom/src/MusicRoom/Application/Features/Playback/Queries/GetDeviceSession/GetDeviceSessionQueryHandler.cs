using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Queries.GetDeviceSession;

/// <summary>
/// Reads a device's live session, for its owner or its controller.
/// </summary>
internal sealed class GetDeviceSessionQueryHandler
    : IRequestHandler<GetDeviceSessionQuery, PlaybackSessionDetail?>
{
    private readonly IPlaybackSessionRepository _sessions;
    private readonly IDeviceRepository _devices;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the session read path, the device read path and the caller.</summary>
    /// <param name="sessions">Where sessions live.</param>
    /// <param name="devices">Where the owner and the controller are read.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetDeviceSessionQueryHandler(
        IPlaybackSessionRepository sessions,
        IDeviceRepository devices,
        ICurrentUserAccessor currentUser)
    {
        _sessions = sessions;
        _devices = devices;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The live session, or null when there is none or the caller may not ask.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaybackSessionDetail?> Handle(
        GetDeviceSessionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetDeviceSessionQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        // `FindControlAsync` answers only for the owner or the current controller, so this one read is also the
        // gate — the same read the claim route publishes an `ETag` from.
        DeviceControl? control = await _devices
            .FindControlAsync(request.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (control is null)
        {
            return null;
        }

        return await _sessions
            .FindByDeviceAsync(request.DeviceId, cancellationToken)
            .ConfigureAwait(false);
    }
}
