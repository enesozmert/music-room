using MediatR;
using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Application.Features.Catalog.Queries.GetTrack;

/// <summary>
/// Resolves one video — `GET /api/v1/tracks/{youtubeVideoId}`.
/// </summary>
/// <param name="YoutubeVideoId">The video id.</param>
/// <remarks>
/// The cheap route: both caches first, and a `videos.list` costing one unit when they miss. It carries no rate
/// limit of its own, because a page that resolves twenty rows is legitimate and limiting it would buy nothing
/// (SPEC-025 decision 8).
/// </remarks>
public sealed record GetTrackQuery(string YoutubeVideoId) : IRequest<CatalogResult<TrackMetadata>>;
