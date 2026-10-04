using Microsoft.Extensions.Caching.Memory;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Infrastructure.Catalog;

/// <summary>
/// ADR-0023's two cache levels, in front of the provider: L1 in process, L2 the database, then YouTube.
/// </summary>
/// <remarks>
/// The one place that decides whether the provider is called. A handler depends on <see cref="ICatalog"/> and
/// therefore cannot spend quota by accident, which is the point of the decorator (SPEC-025 decision 2).
/// <para>
/// A provider answer populates **both** levels. L1 alone would burn the quota again after every restart, and
/// restarts cluster right before a demo — the failure mode ADR-0023 exists to prevent.
/// </para>
/// <para>
/// Entries refresh by age and nothing is invalidated by hand. A stale thumbnail is not a defect worth two
/// invalidation paths.
/// </para>
/// </remarks>
internal sealed class CachedCatalog : ICatalog
{
    private readonly ICatalogProvider _provider;
    private readonly ITrackRefRepository _repository;
    private readonly IMemoryCache _memory;
    private readonly CatalogOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Takes the provider, both cache levels and the clock.</summary>
    /// <param name="provider">The provider, which costs quota.</param>
    /// <param name="repository">L2, the <c>track_refs</c> and <c>track_search_cache</c> tables.</param>
    /// <param name="memory">L1, this process's cache.</param>
    /// <param name="options">The lifetimes.</param>
    /// <param name="time">The clock, injected so a test can age an entry without waiting.</param>
    /// <remarks>
    /// Public on an internal type: the container activates the registered implementation through a public
    /// constructor only.
    /// </remarks>
    public CachedCatalog(
        ICatalogProvider provider,
        ITrackRefRepository repository,
        IMemoryCache memory,
        CatalogOptions options,
        TimeProvider time)
    {
        _provider = provider;
        _repository = repository;
        _memory = memory;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<CatalogResult<TrackMetadata>> GetAsync(
        string youtubeVideoId,
        CancellationToken cancellationToken)
    {
        if (_memory.TryGetValue(MemoryKey(youtubeVideoId), out TrackMetadata? hot) && hot is not null)
        {
            return CatalogResult<TrackMetadata>.Found(hot);
        }

        IReadOnlyList<CachedTrack> stored = await _repository
            .FindAsync([youtubeVideoId], cancellationToken)
            .ConfigureAwait(false);

        DateTime nowUtc = _time.GetUtcNow().UtcDateTime;
        CachedTrack? warm = stored.FirstOrDefault();

        if (warm is not null && nowUtc - warm.FetchedAtUtc < _options.TrackLifetime)
        {
            Remember(warm.Track);

            return CatalogResult<TrackMetadata>.Found(warm.Track);
        }

        CatalogResult<IReadOnlyList<TrackMetadata>> fresh = await _provider
            .GetAsync([youtubeVideoId], cancellationToken)
            .ConfigureAwait(false);

        if (fresh.Outcome == CatalogOutcome.Unavailable)
        {
            // A stale row beats no answer: the provider being down must not hide a track the system already
            // knows — `youtube-integration`'s degradation rule.
            return warm is not null
                ? CatalogResult<TrackMetadata>.Found(warm.Track)
                : CatalogResult<TrackMetadata>.Unavailable();
        }

        TrackMetadata? resolved = fresh.Value?.FirstOrDefault();

        if (resolved is null)
        {
            return CatalogResult<TrackMetadata>.Missing();
        }

        await SaveAsync([resolved], cancellationToken).ConfigureAwait(false);

        return CatalogResult<TrackMetadata>.Found(resolved);
    }

    /// <inheritdoc />
    public async Task<CatalogResult<IReadOnlyList<TrackMetadata>>> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        string normalised = TrackSearchCache.Normalise(query);
        DateTime nowUtc = _time.GetUtcNow().UtcDateTime;

        CatalogResult<IReadOnlyList<string>> ids = await RankedIdsAsync(
            normalised,
            nowUtc,
            page * pageSize,
            cancellationToken).ConfigureAwait(false);

        if (ids.Outcome != CatalogOutcome.Found || ids.Value is null)
        {
            return CatalogResult<IReadOnlyList<TrackMetadata>>.Unavailable();
        }

        IReadOnlyList<string> wanted = [.. ids.Value.Skip((page - 1) * pageSize).Take(pageSize)];

        return wanted.Count == 0
            ? CatalogResult<IReadOnlyList<TrackMetadata>>.Found([])
            : await ResolveAsync(wanted, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The ids for a query: the stored mapping while it is fresh, otherwise one `search.list`.
    /// </summary>
    /// <remarks>
    /// This is the method the expensive call hides behind. A repeated search inside the mapping's lifetime costs
    /// nothing, which is the requirement ADR-0023 states in one line.
    /// </remarks>
    private async Task<CatalogResult<IReadOnlyList<string>>> RankedIdsAsync(
        string normalisedQuery,
        DateTime nowUtc,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        CachedSearch? stored = await _repository
            .FindSearchAsync(normalisedQuery, cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null && nowUtc - stored.FetchedAtUtc < _options.SearchLifetime)
        {
            return CatalogResult<IReadOnlyList<string>>.Found(stored.YoutubeVideoIds);
        }

        CatalogResult<IReadOnlyList<string>> fresh = await _provider
            .SearchAsync(normalisedQuery, maximumResults, cancellationToken)
            .ConfigureAwait(false);

        if (fresh.Outcome != CatalogOutcome.Found || fresh.Value is null)
        {
            // Same degradation rule as a single track: a stale ranking beats nothing at all.
            return stored is not null
                ? CatalogResult<IReadOnlyList<string>>.Found(stored.YoutubeVideoIds)
                : CatalogResult<IReadOnlyList<string>>.Unavailable();
        }

        await _repository
            .SaveSearchAsync(normalisedQuery, fresh.Value, cancellationToken)
            .ConfigureAwait(false);

        return fresh;
    }

    /// <summary>
    /// Resolves ids to metadata, asking the provider only for what neither level has fresh.
    /// </summary>
    private async Task<CatalogResult<IReadOnlyList<TrackMetadata>>> ResolveAsync(
        IReadOnlyList<string> youtubeVideoIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        Dictionary<string, TrackMetadata> known = await CachedAsync(
            youtubeVideoIds,
            nowUtc,
            cancellationToken).ConfigureAwait(false);

        await FillFromProviderAsync(youtubeVideoIds, known, cancellationToken).ConfigureAwait(false);

        // The provider's ranking is the order the caller sees, and an id the provider no longer knows simply
        // drops out rather than becoming a gap the client has to interpret.
        IReadOnlyList<TrackMetadata> page =
        [
            .. youtubeVideoIds
                .Where(known.ContainsKey)
                .Select(id => known[id]),
        ];

        return CatalogResult<IReadOnlyList<TrackMetadata>>.Found(page);
    }

    /// <summary>
    /// What L1 and L2 already hold, fresh — the half of a search that costs no quota.
    /// </summary>
    private async Task<Dictionary<string, TrackMetadata>> CachedAsync(
        IReadOnlyList<string> youtubeVideoIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        Dictionary<string, TrackMetadata> known = [];

        foreach (string id in youtubeVideoIds)
        {
            if (_memory.TryGetValue(MemoryKey(id), out TrackMetadata? hot) && hot is not null)
            {
                known[id] = hot;
            }
        }

        IReadOnlyList<CachedTrack> stored = await _repository
            .FindAsync([.. youtubeVideoIds.Where(id => !known.ContainsKey(id))], cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<TrackMetadata> usable = stored
            .Where(entry => nowUtc - entry.FetchedAtUtc < _options.TrackLifetime)
            .Select(entry => entry.Track);

        foreach (TrackMetadata track in usable)
        {
            known[track.YoutubeVideoId] = track;
            Remember(track);
        }

        return known;
    }

    /// <summary>
    /// Asks the provider for what is left, and populates both levels with the answer.
    /// </summary>
    /// <remarks>
    /// One `videos.list` for every missing id, because fifty ids cost the same single unit as one.
    /// </remarks>
    private async Task FillFromProviderAsync(
        IReadOnlyList<string> youtubeVideoIds,
        Dictionary<string, TrackMetadata> known,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missing = [.. youtubeVideoIds.Where(id => !known.ContainsKey(id))];

        if (missing.Count == 0)
        {
            return;
        }

        CatalogResult<IReadOnlyList<TrackMetadata>> answer = await _provider
            .GetAsync(missing, cancellationToken)
            .ConfigureAwait(false);

        if (answer.Value is null)
        {
            // Unavailable, and the caller still gets whatever the caches had — the degradation rule again.
            return;
        }

        await SaveAsync(answer.Value, cancellationToken).ConfigureAwait(false);

        foreach (TrackMetadata track in answer.Value)
        {
            known[track.YoutubeVideoId] = track;
        }
    }

    private static string MemoryKey(string youtubeVideoId) => "catalog:track:" + youtubeVideoId;

    private async Task SaveAsync(IReadOnlyList<TrackMetadata> tracks, CancellationToken cancellationToken)
    {
        await _repository.SaveAsync(tracks, cancellationToken).ConfigureAwait(false);

        foreach (TrackMetadata track in tracks)
        {
            Remember(track);
        }
    }

    private void Remember(TrackMetadata track) =>
        _memory.Set(MemoryKey(track.YoutubeVideoId), track, _options.MemoryLifetime);
}
