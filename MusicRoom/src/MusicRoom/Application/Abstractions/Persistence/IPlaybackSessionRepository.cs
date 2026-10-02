using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// Playback sessions — <c>docs/api/ENDPOINTS.md</c> §7, ADR-0017.
/// </summary>
/// <remarks>
/// The guarded writes carry the `xmin` the caller last read, which is the arbiter of ADR-0017's fourth
/// concurrency hotspot: two clients advancing at once resolve to one move and one `412`, never a double skip.
/// </remarks>
public interface IPlaybackSessionRepository
{
    /// <summary>Opens a session, or reports that the device already has a live one.</summary>
    /// <param name="session">The session, with its first now-playing already chosen.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the row was written, false when the filtered unique index refused it.</returns>
    Task<bool> TryStartAsync(PlaybackSession session, CancellationToken cancellationToken);

    /// <summary>One session by id.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The session, live or ended, or null.</returns>
    Task<PlaybackSessionDetail?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The live session of one device.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The live session, or null when the device is not playing.</returns>
    Task<PlaybackSessionDetail?> FindByDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Moves a live session's now-playing, if the caller still holds its version.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="nowPlayingId">What to play.</param>
    /// <param name="expectedVersion">The `xmin` the caller last read.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the row moved.</returns>
    Task<bool> TrySetNowPlayingAsync(
        Guid sessionId,
        Guid nowPlayingId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Ends a live session, if the caller still holds its version.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="expectedVersion">The `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the row was ended.</returns>
    /// <remarks>
    /// Used both by the stop route and by an advance that found nothing left, which is the same state change
    /// reached two ways — SPEC-021 decision 5.
    /// </remarks>
    Task<bool> TryEndAsync(
        Guid sessionId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Ends the live session of a device, for its owner.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was ended.</returns>
    /// <remarks>
    /// Unguarded on purpose: stopping playback is idempotent and always correct, and a precondition would let a
    /// stale token keep a device playing longer than its owner wanted.
    /// </remarks>
    Task<bool> TryEndByDeviceAsync(Guid deviceId, DateTime nowUtc, CancellationToken cancellationToken);
}
