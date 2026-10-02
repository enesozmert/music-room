using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>One playback session, as a caller who may see its source reads it.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="DeviceId">The device doing the playing.</param>
/// <param name="SourceType">Whether the source is an event or a playlist.</param>
/// <param name="SourceId">The event or the playlist.</param>
/// <param name="NowPlayingId">What is playing now, or null once the queue is exhausted.</param>
/// <param name="StartedAtUtc">When playback began.</param>
/// <param name="EndedAtUtc">When it stopped, or null while it is live.</param>
/// <param name="Version">The `xmin`, which the endpoint publishes as the `ETag`.</param>
public sealed record PlaybackSessionDetail(
    Guid SessionId,
    Guid DeviceId,
    PlaybackSourceType SourceType,
    Guid SourceId,
    Guid? NowPlayingId,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    uint Version);

/// <summary>What to play, and from where.</summary>
/// <param name="DeviceId">The device, which the caller must own.</param>
/// <param name="SourceType">Whether the source is an event or a playlist.</param>
/// <param name="SourceId">The event or the playlist.</param>
public sealed record PlaybackSource(Guid DeviceId, PlaybackSourceType SourceType, Guid SourceId);

/// <summary>What a guarded playback write did, and where the session now is.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="NowPlayingId">What is playing after the write, when anything is.</param>
/// <param name="SessionId">The session the write touched.</param>
public sealed record PlaybackWriteResult(
    PlaybackWriteOutcome Outcome,
    Guid? NowPlayingId,
    Guid? SessionId)
{
    /// <summary>No such session, or none this caller may command.</summary>
    public static PlaybackWriteResult NotFound { get; } = new(PlaybackWriteOutcome.NotFound, null, null);

    /// <summary>A stale `If-Match`: somebody else moved the session first.</summary>
    public static PlaybackWriteResult Stale { get; } = new(PlaybackWriteOutcome.VersionMismatch, null, null);
}

/// <summary>The ways a playback write ends.</summary>
public enum PlaybackWriteOutcome
{
    /// <summary>No such session, or none this caller may command — the endpoint answers `404`.</summary>
    NotFound = 0,

    /// <summary>Written: the session moved.</summary>
    Advanced = 1,

    /// <summary>
    /// The caller's `expectedNowPlayingId` was already stale, so nothing moved — the endpoint answers `200`
    /// with the current state. A retry from a flaky network must not skip a track (SPEC-021 decision 4).
    /// </summary>
    AlreadyMoved = 2,

    /// <summary>Somebody else moved it first — the endpoint answers `412`.</summary>
    VersionMismatch = 3,

    /// <summary>
    /// Nothing left to play. The session is ended and the endpoint answers `409`, which satisfies both the
    /// contract's status and SDD §11.2's state machine (SPEC-021 decision 5).
    /// </summary>
    QueueExhausted = 4,
}

/// <summary>What plays next in a source, and what plays first.</summary>
/// <remarks>
/// One abstraction over two orderings — an event's highest vote count with ties on the earliest suggestion, and
/// a playlist's next position — so the advance handler contains no branch on source type and each ordering stays
/// beside the query that already expresses it.
/// </remarks>
public interface IPlaybackQueueReader
{
    /// <summary>The item a source plays next, or its first when nothing is playing yet.</summary>
    /// <param name="sourceType">Event or playlist.</param>
    /// <param name="sourceId">The source.</param>
    /// <param name="afterItemId">
    /// What is playing now, or null to ask for the first item. An item that is no longer in the source — a
    /// removed suggestion, a deleted track — is treated as null, so a session whose track vanished resumes at
    /// the top rather than stalling.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The item to play, or null when the source holds nothing more.</returns>
    Task<Guid?> FindNextAsync(
        PlaybackSourceType sourceType,
        Guid sourceId,
        Guid? afterItemId,
        CancellationToken cancellationToken);

    /// <summary>Whether a source holds a given item.</summary>
    /// <param name="sourceType">Event or playlist.</param>
    /// <param name="sourceId">The source.</param>
    /// <param name="itemId">The suggestion or playlist track.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the source holds it, which is what makes an explicit jump safe.</returns>
    Task<bool> ContainsAsync(
        PlaybackSourceType sourceType,
        Guid sourceId,
        Guid itemId,
        CancellationToken cancellationToken);
}
