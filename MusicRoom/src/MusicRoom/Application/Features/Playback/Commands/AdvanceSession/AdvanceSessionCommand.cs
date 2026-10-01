using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Commands.AdvanceSession;

/// <summary>
/// The track ended; play the next one — `POST /api/v1/playback-sessions/{sessionId}/advances`.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <param name="ExpectedNowPlayingId">What the client believes is playing.</param>
/// <param name="ExpectedVersion">The `ETag` the caller last read, from `If-Match`.</param>
/// <remarks>
/// The client is the authority on **when** a track ended and never on **what** follows: the server picks the next
/// item from the source's own order — the highest vote count for an event, the next position for a playlist.
/// </remarks>
public sealed record AdvanceSessionCommand(
    Guid SessionId,
    Guid? ExpectedNowPlayingId,
    uint ExpectedVersion) : IRequest<PlaybackWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playback.advance";

    /// <inheritdoc />
    public string AuditTargetType => "playback-session";
}
