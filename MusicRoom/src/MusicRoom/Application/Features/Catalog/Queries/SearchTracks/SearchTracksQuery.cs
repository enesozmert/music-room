using MediatR;
using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Application.Features.Catalog.Queries.SearchTracks;

/// <summary>
/// Searches the catalog — `GET /api/v1/tracks`.
/// </summary>
/// <param name="Query">What the caller typed.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many tracks.</param>
/// <remarks>
/// Not auditable: a search changes nothing. It is the only route that can reach `search.list`, which is why it
/// carries a per-account rate limit and why the mapping it produces is cached (SPEC-025 decisions 3 and 8).
/// </remarks>
public sealed record SearchTracksQuery(string Query, int Page, int PageSize)
    : IRequest<CatalogResult<IReadOnlyList<TrackMetadata>>>;
