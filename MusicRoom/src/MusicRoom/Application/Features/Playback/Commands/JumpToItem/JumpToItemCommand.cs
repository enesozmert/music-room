using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Commands.JumpToItem;

/// <summary>
/// Plays a named item of the session's source — `PATCH /api/v1/playback-sessions/{sessionId}`.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <param name="NowPlayingId">The item to play. It must belong to the session's own source.</param>
/// <param name="ExpectedVersion">The `ETag` the caller last read, from `If-Match`.</param>
/// <remarks>
/// The one route where the client does choose what plays, which is why it is guarded and bounded: the controller
/// may jump, and only inside the source already playing.
/// </remarks>
public sealed record JumpToItemCommand(Guid SessionId, Guid NowPlayingId, uint ExpectedVersion)
    : IRequest<PlaybackWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playback.jump";

    /// <inheritdoc />
    public string AuditTargetType => "playback-session";
}
