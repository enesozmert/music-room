using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Queries.GetPlaylist;

/// <summary>
/// Reads one playlist — `GET /api/v1/playlists/{playlistId}`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <remarks>
/// Not auditable: a read changes nothing. It answers null when the caller may not see it, which the endpoint
/// turns into `404` — never `403`, because that would confirm the playlist exists.
/// </remarks>
public sealed record GetPlaylistQuery(Guid PlaylistId) : IRequest<PlaylistDetail?>;
