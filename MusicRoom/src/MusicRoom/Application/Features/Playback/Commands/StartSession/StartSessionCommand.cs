using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playback.Commands.StartSession;

/// <summary>
/// Opens a playback session — `POST /api/v1/playback-sessions`.
/// </summary>
/// <param name="Source">The device, and what it is to play.</param>
/// <remarks>
/// The device's **owner** starts playback, and the source must be one they may see. The first now-playing is
/// chosen by the server from the source's own order — ADR-0017.
/// </remarks>
public sealed record StartSessionCommand(PlaybackSource Source)
    : IRequest<SessionStart>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playback.start";

    /// <inheritdoc />
    public string AuditTargetType => "playback-session";
}

/// <summary>What starting answered.</summary>
/// <param name="Outcome">Started, refused, or the device is already playing.</param>
/// <param name="SessionId">The new session, when one was opened.</param>
/// <param name="NowPlayingId">What it started on, when anything was there to play.</param>
public sealed record SessionStart(SessionStartOutcome Outcome, Guid? SessionId, Guid? NowPlayingId);

/// <summary>The three ways starting a session ends.</summary>
public enum SessionStartOutcome
{
    /// <summary>No such device for this caller, or a source they may not see — the endpoint answers `404`.</summary>
    NotPermitted = 0,

    /// <summary>Started.</summary>
    Started = 1,

    /// <summary>The device already has a live session — the endpoint answers `409`.</summary>
    AlreadyPlaying = 2,
}
