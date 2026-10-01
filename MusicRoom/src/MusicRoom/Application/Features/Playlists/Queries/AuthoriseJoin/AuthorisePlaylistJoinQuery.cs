using MediatR;

namespace MusicRoom.Application.Features.Playlists.Queries.AuthoriseJoin;

/// <summary>
/// May this caller listen to a playlist? — `PlaylistHub.JoinPlaylist`, ENDPOINTS §9.
/// </summary>
/// <param name="PlaylistId">The playlist the client asked to join.</param>
/// <remarks>
/// A query rather than a repository call from the hub: `signalr-realtime` says a hub validates its
/// arguments, dispatches a CQRS message and returns, so the gate is decided in the one place REST also asks.
/// A client asking to join `playlist:42` proves nothing.
/// </remarks>
public sealed record AuthorisePlaylistJoinQuery(Guid PlaylistId) : IRequest<bool>;
