using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The three guarded track writes, each already inside the caller's transaction.
/// </summary>
/// <remarks>
/// Every write ends up as one guarded <c>UPDATE</c> of the **playlist** row, keyed on the `xmin` the caller
/// last read, followed by the change to the tracks. The playlist row carries the version a collection cannot
/// carry itself, so that one statement is both the `If-Match` check and the lock that serialises two
/// concurrent reorders: the loser's predicate no longer matches and it answers `412` without touching a track.
/// <para>
/// Every refusal is decided **before** that statement runs, so a refused write leaves the playlist's `ETag`
/// exactly as the caller found it.
/// </para>
/// </remarks>
internal sealed class PlaylistTrackWriter
{
    private readonly MusicRoomDbContext _context;
    private readonly PlaylistTrackPositions _positions;

    /// <summary>Takes the scoped context and the position arithmetic over it.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <param name="positions">Where a track goes, and the renumber that makes room.</param>
    public PlaylistTrackWriter(MusicRoomDbContext context, PlaylistTrackPositions positions)
    {
        _context = context;
        _positions = positions;
    }

    /// <summary>Adds a track at the named destination.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="track">What to add, and where.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, and the new track's id on success.</returns>
    public async Task<TrackWriteResult> AddAsync(
        Guid playlistId,
        TrackPlacement track,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        TrackSlot slot = new(track.AfterTrackId, null, track.AtHead, null);

        if (await _positions.NeighbourMissingAsync(playlistId, slot, cancellationToken).ConfigureAwait(false))
        {
            return TrackWriteResult.NotFound;
        }

        // Asked before the guard, so a full playlist answers `409` without disturbing the version. A racing
        // add cannot slip past it: that add touches the playlist row too, which makes this caller stale.
        int held = await _context.PlaylistTracks
            .CountAsync(existing => existing.PlaylistId == playlistId, cancellationToken)
            .ConfigureAwait(false);

        if (held >= PlaylistTrack.MaximumTracksPerPlaylist)
        {
            return TrackWriteResult.Full;
        }

        TrackWriteResult? refusal = await TouchParentAsync(
                playlistId, expectedVersion, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        long? position = await _positions.PlaceAsync(playlistId, slot, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        return position is long at
            ? await InsertAsync(playlistId, track, at, nowUtc, cancellationToken).ConfigureAwait(false)
            : TrackWriteResult.NotFound;
    }

    /// <summary>Moves a track to the named destination.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="move">Which track moves, and where to.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<TrackWriteResult> MoveAsync(
        Guid playlistId,
        TrackMove move,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        TrackSlot slot = new(move.AfterTrackId, move.BeforeTrackId, AtHead: false, move.PlaylistTrackId);

        if (await _positions.OfAsync(playlistId, move.PlaylistTrackId, cancellationToken)
                .ConfigureAwait(false) is null
            || await _positions.NeighbourMissingAsync(playlistId, slot, cancellationToken).ConfigureAwait(false))
        {
            return TrackWriteResult.NotFound;
        }

        TrackWriteResult? refusal = await TouchParentAsync(
                playlistId, expectedVersion, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        long? position = await _positions.PlaceAsync(playlistId, slot, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        return position is long to
            ? await RepositionAsync(playlistId, move.PlaylistTrackId, to, nowUtc, cancellationToken)
                .ConfigureAwait(false)
            : TrackWriteResult.NotFound;
    }

    /// <summary>Removes a track.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="playlistTrackId">The track.</param>
    /// <param name="expectedVersion">The playlist `xmin` the caller last read.</param>
    /// <param name="nowUtc">The instant the playlist row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<TrackWriteResult> RemoveAsync(
        Guid playlistId,
        Guid playlistTrackId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (await _positions.OfAsync(playlistId, playlistTrackId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return TrackWriteResult.NotFound;
        }

        TrackWriteResult? refusal = await TouchParentAsync(
                playlistId, expectedVersion, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        await _context.PlaylistTracks
            .Where(track => track.PlaylistId == playlistId && track.Id == playlistTrackId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Removing leaves a hole in the sparse sequence, which is what a sparse sequence is for. Nothing is
        // renumbered: the order is unchanged and the next insert reuses the gap.
        return new TrackWriteResult(TrackWriteOutcome.Written, playlistTrackId);
    }

    /// <summary>
    /// Updates the playlist row if the caller still holds its version — the arbiter of every track write.
    /// </summary>
    /// <returns>Null when the caller may proceed, otherwise the refusal to answer with.</returns>
    private async Task<TrackWriteResult?> TouchParentAsync(
        Guid playlistId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int updated = await _context.Playlists
            .Where(playlist => playlist.Id == playlistId && playlist.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(playlist => playlist.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated > 0)
        {
            return null;
        }

        bool stillThere = await _context.Playlists
            .AsNoTracking()
            .AnyAsync(playlist => playlist.Id == playlistId, cancellationToken)
            .ConfigureAwait(false);

        return stillThere ? TrackWriteResult.Stale : TrackWriteResult.NotFound;
    }

    private async Task<TrackWriteResult> RepositionAsync(
        Guid playlistId,
        Guid playlistTrackId,
        long position,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await _context.PlaylistTracks
            .Where(track => track.PlaylistId == playlistId && track.Id == playlistTrackId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(track => track.Position, position)
                    .SetProperty(track => track.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return new TrackWriteResult(TrackWriteOutcome.Written, playlistTrackId);
    }

    private async Task<TrackWriteResult> InsertAsync(
        Guid playlistId,
        TrackPlacement track,
        long position,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        PlaylistTrack row = new()
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            YoutubeVideoId = track.YoutubeVideoId,
            Position = position,
            AddedByUserId = track.AddedByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.PlaylistTracks.Add(row);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new TrackWriteResult(TrackWriteOutcome.Written, row.Id);
    }
}
