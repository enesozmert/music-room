using MediatR;

namespace MusicRoom.Application.Features.Playlists.Queries.ListTracks;

/// <summary>
/// Lists a playlist's tracks in order — `GET /api/v1/playlists/{playlistId}/tracks`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
/// <remarks>
/// Not auditable: a read changes nothing. It answers null when the caller may not see the playlist, which the
/// endpoint turns into `404`.
/// </remarks>
public sealed record ListTracksQuery(Guid PlaylistId, int Page, int PageSize)
    : IRequest<PlaylistTrackPage?>;
