using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Queries.GetSession;

/// <summary>
/// Reads one playback session — `GET /api/v1/playback-sessions/{sessionId}`.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <remarks>
/// The `member` gate of whatever is playing: a session must not become a way to read a private event or playlist,
/// and the id of what is playing is content. A caller who may not see the source gets null, which the endpoint
/// turns into `404`.
/// </remarks>
public sealed record GetSessionQuery(Guid SessionId) : IRequest<PlaybackSessionDetail?>;
