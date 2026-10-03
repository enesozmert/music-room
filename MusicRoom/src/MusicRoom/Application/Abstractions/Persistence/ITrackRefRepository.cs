using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The persistent half of the catalog cache — L2 in ADR-0023.
/// </summary>
/// <remarks>
/// These rows are the cache and the reference data at once, which is why they are in the database rather than
/// only in memory: a restart must not re-burn the provider's quota, and restarts cluster right before a demo.
/// </remarks>
public interface ITrackRefRepository
{
    /// <summary>
    /// Reads the references for these ids, with the age of each.
    /// </summary>
    /// <param name="youtubeVideoIds">The ids to look for.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per id that is stored; an unknown id is absent.</returns>
    Task<IReadOnlyList<CachedTrack>> FindAsync(
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a stored search mapping.
    /// </summary>
    /// <param name="normalisedQuery">The query, normalised by <c>TrackSearchCache.Normalise</c>.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The mapping, or null when this query was never searched.</returns>
    Task<CachedSearch?> FindSearchAsync(string normalisedQuery, CancellationToken cancellationToken);

    /// <summary>
    /// Writes what the provider answered: inserts a new reference, refreshes one that exists.
    /// </summary>
    /// <param name="tracks">The metadata to store.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    /// <remarks>
    /// An upsert rather than a read-then-write: two requests resolving the same video at once must not be a
    /// unique-index violation, so the database arbitrates — the posture SPEC-015 and SPEC-018 use for every
    /// contended write.
    /// </remarks>
    Task SaveAsync(IReadOnlyList<TrackMetadata> tracks, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a search mapping, replacing the ids if the query is already stored.
    /// </summary>
    /// <param name="normalisedQuery">The normalised query.</param>
    /// <param name="youtubeVideoIds">The ids the provider ranked, in order.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task SaveSearchAsync(
        string normalisedQuery,
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// A stored reference and when it was fetched.
/// </summary>
/// <param name="Track">The metadata.</param>
/// <param name="FetchedAtUtc">When the provider last answered for it, so the caller can judge its age.</param>
/// <remarks>
/// The age is returned rather than judged here: the lifetime is configuration, and a repository that applied it
/// would decide policy the cache owns.
/// </remarks>
public sealed record CachedTrack(TrackMetadata Track, DateTime FetchedAtUtc);

/// <summary>
/// A stored search mapping and when it was fetched.
/// </summary>
/// <param name="YoutubeVideoIds">The ids, in the order the provider ranked them.</param>
/// <param name="FetchedAtUtc">When the search last ran.</param>
public sealed record CachedSearch(IReadOnlyList<string> YoutubeVideoIds, DateTime FetchedAtUtc);
