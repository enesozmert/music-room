using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// What is playing now changed — `NowPlayingChanged`, SDD §9.2, ADR-0017.
/// </summary>
/// <param name="SourceType">Whether the listeners are grouped by an event or a playlist.</param>
/// <param name="SourceId">The event or the playlist, which is also the group key.</param>
/// <param name="SessionId">The session that moved.</param>
/// <param name="NowPlayingId">What is playing now, or null once the queue is exhausted and the session ended.</param>
/// <param name="OccurredAtUtc">Server time when the move committed.</param>
/// <param name="NowPlayingStartedAtUtc">The instant stamped on the session row for this track, or null
/// when nothing is playing — SPEC-058. The same number `GET /playback-sessions/{id}` reports, so a
/// listener and a guest who reads the session a minute later compute the same offset. Distinct from
/// <paramref name="OccurredAtUtc" /> on purpose: that is when this message was raised, this is what the
/// row says, and a client synchronising playback wants the row's answer.</param>
/// <remarks>
/// Broadcast on the hub owning the playback source — `EventHub` or `PlaylistHub` — which is why the payload
/// carries the source type as well as its id.
/// <para>
/// No row version, by ADR-0036's rule: the authoritative value is the now-playing id itself, and applying it
/// twice is applying it once. The session's own `xmin` is the `If-Match` of its writes and would mean nothing to
/// a listener who never holds it. A null id is the honest end of a queue — listeners stop rather than waiting.
/// </para>
/// </remarks>
public sealed record NowPlayingChanged(
    PlaybackSourceType SourceType,
    Guid SourceId,
    Guid SessionId,
    Guid? NowPlayingId,
    DateTime OccurredAtUtc,
    DateTime? NowPlayingStartedAtUtc = null) : IRealtimeBroadcast
{
    /// <inheritdoc />
    public Guid AggregateId => SourceId;
}

/// <summary>
/// The playing device said where it is — `PlaybackPositionChanged`, SPEC-059.
/// </summary>
/// <param name="SourceType">Whether the listeners are grouped by an event or a playlist.</param>
/// <param name="SourceId">The event or the playlist, which is also the group key.</param>
/// <param name="SessionId">The session being reported on.</param>
/// <param name="NowPlayingId">The track the position is measured in. A listener whose own idea of the
/// current track differs ignores the message rather than seeking into a song nobody is playing.</param>
/// <param name="PositionMs">How far into that track the device said it was.</param>
/// <param name="PositionAtUtc">When the report reached the server, by the server's clock. A listener
/// computes `positionMs + (now − positionAtUtc)` while playing, and `positionMs` alone while paused.</param>
/// <param name="Paused">Whether the device said it was paused.</param>
/// <remarks>
/// **Telemetry, not a command.** It carries no row version, by ADR-0036's rule and for a sharper reason
/// than `NowPlayingChanged` has: the next one is along in fifteen seconds, so a message that arrives out
/// of order or not at all costs a listener nothing it will not have again shortly.
/// <para>
/// It is broadcast on the hub that owns the playback source, exactly as `NowPlayingChanged` is.
/// </para>
/// </remarks>
public sealed record PlaybackPositionChanged(
    PlaybackSourceType SourceType,
    Guid SourceId,
    Guid SessionId,
    Guid NowPlayingId,
    int PositionMs,
    DateTime PositionAtUtc,
    bool Paused) : IRealtimeBroadcast
{
    /// <inheritdoc />
    public Guid AggregateId => SourceId;
}
