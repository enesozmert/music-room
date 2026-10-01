using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Playback.Commands.StopSession;

/// <summary>
/// Stops playback — `DELETE /api/v1/playback-sessions/{sessionId}`.
/// </summary>
/// <param name="SessionId">The session.</param>
/// <remarks>
/// The device's owner stops it: a delegate steers what plays and does not get to switch the speaker off. The row
/// is stamped ended rather than deleted, so the audit records that name it keep pointing at something.
/// </remarks>
public sealed record StopSessionCommand(Guid SessionId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playback.stop";

    /// <inheritdoc />
    public string AuditTargetType => "playback-session";
}
