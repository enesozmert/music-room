using MediatR;
using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Application.Features.Catalog.Queries.SearchTracks;

/// <summary>
/// Asks the catalog, which decides whether the provider is touched at all — ADR-0023.
/// </summary>
/// <remarks>
/// Thin on purpose. The cache order, the quota rule and the age policy live behind <see cref="ICatalog"/>, so
/// this handler is the one line it looks like: a handler that reached for the provider itself would be the
/// second place that knows when YouTube may be called.
/// </remarks>
internal sealed class SearchTracksQueryHandler
    : IRequestHandler<SearchTracksQuery, CatalogResult<IReadOnlyList<TrackMetadata>>>
{
    private readonly ICatalog _catalog;

    /// <summary>Takes the catalog.</summary>
    /// <param name="catalog">The cached catalog.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SearchTracksQueryHandler(ICatalog catalog) => _catalog = catalog;

    /// <summary>Searches.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A page of tracks, or the unavailable outcome the route answers `503` for.</returns>
    public Task<CatalogResult<IReadOnlyList<TrackMetadata>>> Handle(
        SearchTracksQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _catalog.SearchAsync(request.Query, request.Page, request.PageSize, cancellationToken);
    }
}
