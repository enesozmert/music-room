using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playback;

/// <summary>
/// Who may command a playback session, and who may read it — ADR-0017, SPEC-020.
/// </summary>
/// <remarks>
/// The session stores no controller. Who may command it is read **live** from the device it plays on:
/// <c>controller_user_id ?? owner_id</c>. That is the concrete meaning of delegating music control, and it means
/// a revoke takes effect on the delegate's next command rather than on their next session.
/// <para>
/// A named collaborator rather than a line repeated in four handlers, for the same reason
/// <c>EventVoteGate</c> is one: a gate asked in several places is a gate somebody will eventually ask only half
/// of.
/// </para>
/// </remarks>
public sealed class PlaybackControlGate
{
    private readonly IDeviceRepository _devices;
    private readonly IEventRepository _events;
    private readonly IPlaylistRepository _playlists;

    /// <summary>Takes the device read path and both source gates.</summary>
    /// <param name="devices">Where the control grant lives.</param>
    /// <param name="events">Where an event's `member` gate is decided.</param>
    /// <param name="playlists">Where a playlist's `member` gate is decided.</param>
    public PlaybackControlGate(
        IDeviceRepository devices,
        IEventRepository events,
        IPlaylistRepository playlists)
    {
        _devices = devices;
        _events = events;
        _playlists = playlists;
    }

    /// <summary>Whether this caller may command playback on a device.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True for the owner while they hold control, and for the delegate while they do.</returns>
    /// <remarks>
    /// Exclusive: once control is delegated the **owner** cannot command it either, which is what makes the
    /// delegation mean anything. The owner's recourse is to revoke, which is one request away.
    /// </remarks>
    public async Task<bool> MayCommandAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        DeviceControl? control = await _devices
            .FindControlAsync(deviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        return control?.ControllerUserId == userId;
    }

    /// <summary>Whether this caller may see what a session is playing.</summary>
    /// <param name="sourceType">Event or playlist.</param>
    /// <param name="sourceId">The source.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the caller may see the source the session plays from.</returns>
    /// <remarks>
    /// The `member` gate of whichever aggregate is playing: a session must not become a way to read a private
    /// event or playlist, and the id of what is playing is content.
    /// </remarks>
    public async Task<bool> MaySeeSourceAsync(
        PlaybackSourceType sourceType,
        Guid sourceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (sourceType == PlaybackSourceType.Event)
        {
            EventAccess access = await _events
                .FindAccessAsync(sourceId, userId, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            return access.MaySee;
        }

        PlaylistAccess playlist = await _playlists
            .FindAccessAsync(sourceId, userId, cancellationToken)
            .ConfigureAwait(false);

        return playlist.MaySee;
    }
}
