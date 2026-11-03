using MediatR;

namespace MusicRoom.Application.Features.Playback.Commands.ReportPosition;

/// <summary>
/// The playing device saying where it is — SPEC-059.
/// </summary>
/// <param name="SessionId">The session being reported on.</param>
/// <param name="NowPlayingId">The track the position is measured in.</param>
/// <param name="PositionMs">How far into that track the device is.</param>
/// <param name="Paused">Whether it is paused.</param>
/// <remarks>
/// **This command does not implement <c>IAuditableCommand</c>, and the omission is the decision** —
/// SPEC-059 decision 3. Subject V.6 asks for a record of what a person did in the app; a position report
/// is a timer telling the server what the speaker is doing, four times a minute for as long as the music
/// plays. Auditing it would bury the records V.6 exists to produce under telemetry nobody will read.
/// <para>
/// The marker interface was built for exactly this: *"a command that should not be audited must be able
/// to say so by omission rather than by an exclusion list nobody maintains."* A deliberate omission,
/// written down, is not the same as a forgotten one.
/// </para>
/// </remarks>
public sealed record ReportPositionCommand(Guid SessionId, Guid NowPlayingId, int PositionMs, bool Paused)
    : IRequest<bool>;
