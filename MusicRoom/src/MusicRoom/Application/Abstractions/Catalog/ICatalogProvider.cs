namespace MusicRoom.Application.Abstractions.Catalog;

/// <summary>
/// The music provider itself, with no cache in front of it — ADR-0003.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>Infrastructure</c>, so no layer above learns that the catalog is YouTube
/// (ADR-0028). **Nothing but <see cref="ICatalog"/> should depend on this**: a handler that called the provider
/// directly would bypass both cache levels, which is how a daily quota disappears before the demo.
/// <para>
/// Every method here costs quota. `search.list` is roughly a hundred times a `videos.list` by id, which is why
/// the two are separate methods rather than one search that happens to take ids.
/// </para>
/// </remarks>
public interface ICatalogProvider
{
    /// <summary>
    /// Searches the provider — the expensive call.
    /// </summary>
    /// <param name="query">What the caller typed.</param>
    /// <param name="maximumResults">How many ids to ask for.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The ids the provider ranked, or an unavailable result.</returns>
    Task<CatalogResult<IReadOnlyList<string>>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves ids to metadata in one call — the cheap one.
    /// </summary>
    /// <param name="youtubeVideoIds">The ids to resolve. One call covers all of them.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Metadata for the ids the provider knows; an id it does not know is simply absent.</returns>
    Task<CatalogResult<IReadOnlyList<TrackMetadata>>> GetAsync(
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken);
}
