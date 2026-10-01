using Microsoft.EntityFrameworkCore;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// Where a track goes: the arithmetic of the sparse position key, and the renumber that rescues it.
/// </summary>
/// <remarks>
/// A position is a <c>bigint</c> and neighbours are nominally 1000 apart, so an insert between two tracks
/// writes one row. Repeated inserts into the same gap halve it; when nothing is left between two neighbours
/// the whole playlist is rewritten at gap 1000. That rewrite is bounded by the 500-track cap, and it runs
/// inside the caller's transaction, after the `xmin` guard has already decided the caller is not stale.
/// </remarks>
internal sealed class PlaylistTrackPositions
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    public PlaylistTrackPositions(MusicRoomDbContext context) => _context = context;

    /// <summary>The position between two neighbours, or null when no integer fits between them.</summary>
    /// <param name="lower">The neighbour below, or null when the destination is the head.</param>
    /// <param name="upper">The neighbour above, or null when the destination is the tail.</param>
    /// <returns>The new position, or null to say the playlist must be renumbered first.</returns>
    /// <remarks>
    /// The head case halves the first position rather than subtracting the gap, which keeps every position
    /// positive and so keeps a renumber the only way the sequence can run out of room.
    /// </remarks>
    public static long? Between(long? lower, long? upper) => (lower, upper) switch
    {
        (null, null) => PlaylistTrack.PositionGap,
        (null, long first) => first >= 2 ? first / 2 : null,
        (long last, null) => last + PlaylistTrack.PositionGap,
        (long below, long above) => above - below >= 2 ? below + ((above - below) / 2) : null,
    };

    /// <summary>The position of a track, when it is in this playlist.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="playlistTrackId">The track.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The position, or null when the playlist does not hold that track.</returns>
    public async Task<long?> OfAsync(
        Guid playlistId,
        Guid playlistTrackId,
        CancellationToken cancellationToken) =>
        await _context.PlaylistTracks
            .AsNoTracking()
            .Where(track => track.PlaylistId == playlistId && track.Id == playlistTrackId)
            .Select(track => (long?)track.Position)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The first position in the playlist, or null when it is empty.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The smallest position, or null.</returns>
    public Task<long?> FirstAsync(Guid playlistId, CancellationToken cancellationToken) =>
        BoundAsync(playlistId, ascending: true, cancellationToken);

    /// <summary>The last position in the playlist, or null when it is empty.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The largest position, or null.</returns>
    public Task<long?> LastAsync(Guid playlistId, CancellationToken cancellationToken) =>
        BoundAsync(playlistId, ascending: false, cancellationToken);

    /// <summary>The nearest position above <paramref name="position"/>, or null when there is none.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="position">The position to look above.</param>
    /// <param name="excludedTrackId">A track to ignore — the one that is moving.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The next position, or null at the tail.</returns>
    public async Task<long?> AboveAsync(
        Guid playlistId,
        long position,
        Guid? excludedTrackId,
        CancellationToken cancellationToken) =>
        await Neighbours(playlistId, excludedTrackId)
            .Where(track => track.Position > position)
            .OrderBy(track => track.Position)
            .Select(track => (long?)track.Position)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The nearest position below <paramref name="position"/>, or null when there is none.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="position">The position to look below.</param>
    /// <param name="excludedTrackId">A track to ignore — the one that is moving.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The previous position, or null at the head.</returns>
    public async Task<long?> BelowAsync(
        Guid playlistId,
        long position,
        Guid? excludedTrackId,
        CancellationToken cancellationToken) =>
        await Neighbours(playlistId, excludedTrackId)
            .Where(track => track.Position < position)
            .OrderByDescending(track => track.Position)
            .Select(track => (long?)track.Position)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Rewrites every position in the playlist at gap 1000, keeping the current order.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    /// <remarks>
    /// Tracked rows and one <c>SaveChangesAsync</c>, not <c>ExecuteUpdateAsync</c>: each row gets its own
    /// value, so there is nothing to express as a single set clause. At most 500 rows by the cap.
    /// </remarks>
    public async Task RenumberAsync(Guid playlistId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<PlaylistTrack> tracks = await _context.PlaylistTracks
            .Where(track => track.PlaylistId == playlistId)
            .OrderBy(track => track.Position)
            .ThenBy(track => track.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        for (int index = 0; index < tracks.Count; index++)
        {
            tracks[index].Position = (index + 1) * PlaylistTrack.PositionGap;
            tracks[index].UpdatedAtUtc = nowUtc;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The position for one destination, renumbering once if the gap has run out.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="slot">Where the track is going.</param>
    /// <param name="nowUtc">The instant a renumber stamps its rows with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The position, or null when the named neighbour is not in the playlist.</returns>
    public async Task<long?> PlaceAsync(
        Guid playlistId,
        TrackSlot slot,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        (long? Lower, long? Upper)? bounds = await BoundsAsync(playlistId, slot, cancellationToken)
            .ConfigureAwait(false);

        long? position = bounds is null
            ? null
            : Between(bounds.Value.Lower, bounds.Value.Upper);

        if (bounds is null || position is not null)
        {
            return position;
        }

        // Nothing fits between the neighbours any more. Rewriting the whole playlist at gap 1000 restores room
        // everywhere at once, and the bounds have to be read again because every position has changed.
        await RenumberAsync(playlistId, nowUtc, cancellationToken).ConfigureAwait(false);

        bounds = await BoundsAsync(playlistId, slot, cancellationToken).ConfigureAwait(false);

        return bounds is null
            ? null
            : Between(bounds.Value.Lower, bounds.Value.Upper);
    }

    /// <summary>The two positions a destination sits between.</summary>
    private async Task<(long? Lower, long? Upper)?> BoundsAsync(
        Guid playlistId,
        TrackSlot slot,
        CancellationToken cancellationToken)
    {
        if (slot.AfterTrackId is Guid after)
        {
            long? lower = await OfAsync(playlistId, after, cancellationToken).ConfigureAwait(false);

            return lower is null
                ? null
                : (lower, await AboveAsync(
                        playlistId, lower.Value, slot.MovingTrackId, cancellationToken)
                    .ConfigureAwait(false));
        }

        if (slot.BeforeTrackId is Guid before)
        {
            long? upper = await OfAsync(playlistId, before, cancellationToken).ConfigureAwait(false);

            return upper is null
                ? null
                : (await BelowAsync(
                        playlistId, upper.Value, slot.MovingTrackId, cancellationToken)
                    .ConfigureAwait(false), upper);
        }

        return slot.AtHead
            ? (null, await FirstAsync(playlistId, cancellationToken).ConfigureAwait(false))
            : (await LastAsync(playlistId, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>Whether the destination names a track this playlist does not hold.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="slot">The destination, which may name a neighbour.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a named neighbour is absent, which the caller answers `404` to.</returns>
    public async Task<bool> NeighbourMissingAsync(
        Guid playlistId,
        TrackSlot slot,
        CancellationToken cancellationToken) =>
        (slot.AfterTrackId ?? slot.BeforeTrackId) is Guid neighbour
        && await OfAsync(playlistId, neighbour, cancellationToken).ConfigureAwait(false) is null;

    private IQueryable<PlaylistTrack> Neighbours(Guid playlistId, Guid? excludedTrackId) =>
        _context.PlaylistTracks
            .AsNoTracking()
            .Where(track => track.PlaylistId == playlistId
                && (excludedTrackId == null || track.Id != excludedTrackId));

    private async Task<long?> BoundAsync(Guid playlistId, bool ascending, CancellationToken cancellationToken)
    {
        IQueryable<PlaylistTrack> tracks = _context.PlaylistTracks
            .AsNoTracking()
            .Where(track => track.PlaylistId == playlistId);

        return await (ascending
                ? tracks.OrderBy(track => track.Position)
                : tracks.OrderByDescending(track => track.Position))
            .Select(track => (long?)track.Position)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Where a write wants the track to end up.</summary>
/// <param name="AfterTrackId">Put it after this track.</param>
/// <param name="BeforeTrackId">Put it before this track.</param>
/// <param name="AtHead">Put it before everything.</param>
/// <param name="MovingTrackId">The track that is moving, which is not its own neighbour.</param>
/// <remarks>
/// One type instead of four parameters, which keeps the neighbour lookup inside the analyzer's parameter
/// budget and keeps add and move asking the same question.
/// </remarks>
internal sealed record TrackSlot(
    Guid? AfterTrackId,
    Guid? BeforeTrackId,
    bool AtHead,
    Guid? MovingTrackId);
