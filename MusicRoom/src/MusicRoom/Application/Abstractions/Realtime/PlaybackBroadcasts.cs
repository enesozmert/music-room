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
