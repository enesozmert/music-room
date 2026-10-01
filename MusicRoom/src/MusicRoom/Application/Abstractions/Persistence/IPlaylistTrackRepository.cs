using MusicRoom.Application.Features.Playlists.Queries.ListTracks;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The tracks of a playlist — <c>docs/api/ENDPOINTS.md</c> §5.
/// </summary>
/// <remarks>
/// Every write takes the `xmin` the caller last read and **updates the playlist row first**. If that update
/// matches no row the caller is stale, nothing else runs, and the endpoint answers `412`. That is how
/// `If-Match` protects a collection: the parent carries the version its contents do not have.
/// </remarks>
public interface IPlaylistTrackRepository
{
    /// <summary>Adds a track, at the tail or next to a named neighbour.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="track">What to add, and where — see <see cref="TrackPlacement"/>.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, and on success the new track's id.</returns>
    Task<TrackWriteResult> TryAddAsync(
        Guid playlistId,
        TrackPlacement track,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Moves a track next to a named neighbour.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="move">Which track moves, and where to.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome. A stale caller is <see cref="TrackWriteOutcome.VersionMismatch"/>.</returns>
    /// <remarks>
    /// When no gap remains between the neighbours, the playlist is renumbered at gap 1000 in the same
    /// transaction — bounded by the 500-track cap.
    /// </remarks>
    Task<TrackWriteResult> TryMoveAsync(
        Guid playlistId,
        TrackMove move,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Removes a track.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="playlistTrackId">The track.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the playlist row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<TrackWriteResult> TryRemoveAsync(
        Guid playlistId,
        Guid playlistTrackId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Lists a page of tracks, in order, with the collection's version.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page and the playlist's `xmin`.</returns>
    Task<PlaylistTrackPage> ListAsync(
        Guid playlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>What to add, and where.</summary>
/// <param name="YoutubeVideoId">The video id, as the client sent it.</param>
/// <param name="AddedByUserId">Who is adding it.</param>
/// <param name="AfterTrackId">Insert after this track; null means the head when
/// <paramref name="AtHead"/> is set, and the tail otherwise.</param>
/// <param name="AtHead">True to insert before everything.</param>
public sealed record TrackPlacement(
    string YoutubeVideoId,
    Guid AddedByUserId,
    Guid? AfterTrackId,
    bool AtHead);

/// <summary>Which track moves, and where to.</summary>
/// <param name="PlaylistTrackId">The track that moves.</param>
/// <param name="AfterTrackId">Put it after this one, or null.</param>
/// <param name="BeforeTrackId">Put it before this one, or null. Exactly one of the two is set; both or
/// neither is `422`, refused before this type is built.</param>
public sealed record TrackMove(Guid PlaylistTrackId, Guid? AfterTrackId, Guid? BeforeTrackId);

/// <summary>What a guarded track write did, and the row it created.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="PlaylistTrackId">The new track, when one was added.</param>
public sealed record TrackWriteResult(TrackWriteOutcome Outcome, Guid? PlaylistTrackId)
{
    /// <summary>A stale caller: somebody changed the playlist or its tracks first.</summary>
    public static TrackWriteResult Stale { get; } = new(TrackWriteOutcome.VersionMismatch, null);

    /// <summary>No such playlist, or no such track in it.</summary>
    public static TrackWriteResult NotFound { get; } = new(TrackWriteOutcome.NotFound, null);

    /// <summary>The playlist is full.</summary>
    public static TrackWriteResult Full { get; } = new(TrackWriteOutcome.PlaylistFull, null);
}

/// <summary>The four ways a guarded track write ends.</summary>
public enum TrackWriteOutcome
{
    /// <summary>No such playlist, or no such track in it.</summary>
    NotFound = 0,

    /// <summary>Written.</summary>
    Written = 1,

    /// <summary>Somebody changed the collection after the caller read it — the endpoint answers `412`.</summary>
    VersionMismatch = 2,

    /// <summary>The playlist already holds its maximum number of tracks — the endpoint answers `409`.</summary>
    PlaylistFull = 3,
}
