namespace MusicRoom.Application.Abstractions.Catalog;

/// <summary>
/// The catalog as a handler sees it: cached, and quota-disciplined — ADR-0023.
/// </summary>
/// <remarks>
/// The implementation is the two-level cache in front of <see cref="ICatalogProvider"/>: L1 in process, L2 the
/// <c>track_refs</c> table, then the provider, which populates both on the way back. A handler depends on this
/// so the lookup order lives in exactly one place — two handlers each deciding when to call YouTube is two
/// chances to spend the day's quota twice (SPEC-025 decision 2).
/// </remarks>
public interface ICatalog
{
    /// <summary>
    /// Searches, preferring a cached query-to-ids mapping over a provider search.
    /// </summary>
    /// <param name="query">What the caller typed.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many tracks.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A page of metadata, or an unavailable result.</returns>
    Task<CatalogResult<IReadOnlyList<TrackMetadata>>> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves one video, preferring both caches over the provider.
    /// </summary>
    /// <param name="youtubeVideoId">The video id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The track, missing when the catalog has no such video, or unavailable.</returns>
    Task<CatalogResult<TrackMetadata>> GetAsync(string youtubeVideoId, CancellationToken cancellationToken);
}
