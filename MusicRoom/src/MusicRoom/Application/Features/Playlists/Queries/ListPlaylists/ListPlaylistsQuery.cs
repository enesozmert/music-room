using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Queries.ListPlaylists;

/// <summary>
/// Lists playlists in one scope — `GET /api/v1/playlists`.
/// </summary>
/// <param name="Scope">`mine`, `shared` or `public`.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
public sealed record ListPlaylistsQuery(PlaylistScope Scope, int Page, int PageSize)
    : IRequest<IReadOnlyList<PlaylistSummary>>;
