using MediatR;
using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Application.Features.Catalog.Queries.GetTrack;

/// <summary>
/// Reads one track through the cache — ADR-0023's lookup order.
/// </summary>
internal sealed class GetTrackQueryHandler : IRequestHandler<GetTrackQuery, CatalogResult<TrackMetadata>>
{
    private readonly ICatalog _catalog;

    /// <summary>Takes the catalog.</summary>
    /// <param name="catalog">The cached catalog.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetTrackQueryHandler(ICatalog catalog) => _catalog = catalog;

    /// <summary>Resolves it.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The track, missing for a video the catalog does not know, or unavailable.</returns>
    public Task<CatalogResult<TrackMetadata>> Handle(
        GetTrackQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _catalog.GetAsync(request.YoutubeVideoId, cancellationToken);
    }
}
