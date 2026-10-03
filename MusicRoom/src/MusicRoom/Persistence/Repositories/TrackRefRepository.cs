using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The catalog's persistent cache — ADR-0023's L2.
/// </summary>
/// <remarks>
/// The sets are reached with <c>Set&lt;T&gt;()</c> rather than through properties on
/// <see cref="MusicRoomDbContext"/>: that class is at the coupling limit, and these two tables are used by this
/// repository alone. The comment at the end of the context's set list records the same thing.
/// <para>
/// Writes are upserts arbitrated by the unique indexes, not read-then-write. Two requests resolving the same
/// video at the same moment is the ordinary case for a cache, so a race must be a no-op rather than a
/// constraint violation — the posture SPEC-015 and SPEC-018 use for every contended write.
/// </para>
/// </remarks>
internal sealed class TrackRefRepository : ITrackRefRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public TrackRefRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CachedTrack>> FindAsync(
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(youtubeVideoIds);

        if (youtubeVideoIds.Count == 0)
        {
            return [];
        }

        return await _context.Set<TrackRef>()
            .AsNoTracking()
            .Where(track => youtubeVideoIds.Contains(track.YoutubeVideoId))
            .Select(track => new CachedTrack(
                new TrackMetadata(
                    track.YoutubeVideoId,
                    track.Title,
                    track.Channel,
                    track.DurationSeconds,
                    track.ThumbnailUrl),
                track.FetchedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CachedSearch?> FindSearchAsync(
        string normalisedQuery,
        CancellationToken cancellationToken) =>
        await _context.Set<TrackSearchCache>()
            .AsNoTracking()
            .Where(search => search.Query == normalisedQuery)
            .Select(search => new CachedSearch(search.VideoIds, search.FetchedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<TrackMetadata> tracks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count == 0)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        string[] ids = [.. tracks.Select(track => track.YoutubeVideoId)];

        Dictionary<string, TrackRef> stored = await _context.Set<TrackRef>()
            .Where(track => ids.Contains(track.YoutubeVideoId))
            .ToDictionaryAsync(track => track.YoutubeVideoId, cancellationToken)
            .ConfigureAwait(false);

        foreach (TrackMetadata track in tracks)
        {
            if (stored.TryGetValue(track.YoutubeVideoId, out TrackRef? existing))
            {
                Refresh(existing, track, nowUtc);
                continue;
            }

            _context.Set<TrackRef>().Add(New(track, nowUtc));
        }

        await SaveIgnoringARaceAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveSearchAsync(
        string normalisedQuery,
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(youtubeVideoIds);

        DateTime nowUtc = DateTime.UtcNow;

        TrackSearchCache? existing = await _context.Set<TrackSearchCache>()
            .FirstOrDefaultAsync(search => search.Query == normalisedQuery, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _context.Set<TrackSearchCache>().Add(new TrackSearchCache
            {
                Id = Guid.NewGuid(),
                Query = normalisedQuery,
                VideoIds = youtubeVideoIds,
                FetchedAtUtc = nowUtc,
            });
        }
        else
        {
            existing.VideoIds = youtubeVideoIds;
            existing.FetchedAtUtc = nowUtc;
        }

        await SaveIgnoringARaceAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TrackRef New(TrackMetadata track, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeVideoId = track.YoutubeVideoId,
        Title = track.Title,
        Channel = track.Channel,
        DurationSeconds = track.DurationSeconds,
        ThumbnailUrl = track.ThumbnailUrl,
        FetchedAtUtc = nowUtc,
    };

    private static void Refresh(TrackRef existing, TrackMetadata track, DateTime nowUtc)
    {
        existing.Title = track.Title;
        existing.Channel = track.Channel;
        existing.DurationSeconds = track.DurationSeconds;
        existing.ThumbnailUrl = track.ThumbnailUrl;
        existing.FetchedAtUtc = nowUtc;
    }

    /// <summary>
    /// Saves, and treats a unique-index violation as the cache already being warm.
    /// </summary>
    /// <remarks>
    /// Two requests can resolve the same video, or run the same search, in the same instant. Both then insert,
    /// and one loses to `ux_track_refs_youtube_video_id` or `ux_track_search_cache_query`. For a cache that is
    /// not a failure: the row the loser wanted is there, written by the winner. The alternative — surfacing it —
    /// would turn a successful catalog read into a `500` because the cache worked twice.
    /// </remarks>
    private async Task SaveIgnoringARaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Detach what could not be written, so the next use of this scoped context is not poisoned by it.
            _context.ChangeTracker.Clear();
        }
    }
}
