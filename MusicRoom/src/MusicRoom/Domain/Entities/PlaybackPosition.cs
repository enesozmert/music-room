using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// Where the playing device last said it was, in the track it last said it was playing — SPEC-059.
/// </summary>
/// <remarks>
/// **Its own row, beside the session rather than on it.** `playback_sessions.xmin` is the `If-Match` of
/// advance and jump, and a position written to that row would bump the version four times a minute,
/// answering every controller `412` for a value they never asked about. One row per session, upserted,
/// and the session row is never touched by a report.
/// <para>
/// **This is the device's claim, stamped by the server.** The milliseconds are whatever the speaker
/// reported; <see cref="ReportedAtUtc" /> is this server's own clock, because two phones reporting "now"
/// would disagree about the same instant and the backend is the keeper of the truth (subject V.3).
/// </para>
/// <para>
/// **It names the track it belongs to.** A report that was in flight while the track advanced is stale on
/// arrival, and applying it would make every listener seek backwards into the previous song.
/// </para>
/// </remarks>
public sealed class PlaybackPosition : Entity
{
    /// <summary>The session this position belongs to. One row per session.</summary>
    public Guid SessionId { get; set; }

    /// <summary>The suggestion or playlist track the position is measured in.</summary>
    public Guid NowPlayingId { get; set; }

    /// <summary>How far into that track the device said it was, in milliseconds.</summary>
    public int PositionMs { get; set; }

    /// <summary>Whether the device said it was paused when it reported.</summary>
    public bool Paused { get; set; }

    /// <summary>When the report reached this server. Always UTC, and always this server's clock.</summary>
    public DateTime ReportedAtUtc { get; set; }
}
