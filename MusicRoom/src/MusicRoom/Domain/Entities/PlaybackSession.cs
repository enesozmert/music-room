using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// Who is playing what, right now, on which device — ADR-0017.
/// </summary>
/// <remarks>
/// **There is no controller column.** Who may command this session is read from the device:
/// <c>controller_user_id ?? owner_id</c>. ADR-0017 lists a controller on the session, and SPEC-020 then made the
/// device row the single place a grant lives; copying it here would be two rows that can disagree about who
/// controls one device. The ADR's sentence stays true in effect — the grant changes who controls the session, by
/// being the thing the session reads (SPEC-021 decision 1).
/// <para>
/// At most one **live** session per device, enforced by a unique index filtered on
/// <see cref="EndedAtUtc"/> being null. A stopped session keeps its row: "this device played this event last
/// night" is a fact worth having, and a hard delete would leave the audit records that name it pointing at
/// nothing.
/// </para>
/// </remarks>
public sealed class PlaybackSession : Entity
{
    /// <summary>The device doing the playing. The licence is per device — subject V.2.2.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Whether the source is an event or a playlist.</summary>
    public PlaybackSourceType SourceType { get; set; }

    /// <summary>The event or the playlist. Deliberately not a foreign key — SPEC-021 decision 3.</summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// The suggestion or the playlist track playing now, or null once the queue is exhausted.
    /// </summary>
    public Guid? NowPlayingId { get; set; }

    /// <summary>When playback began. Always UTC.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>When it stopped, or null while it is live.</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>PostgreSQL's <c>xmin</c>, the concurrency token. Never set by this application.</summary>
    public uint Version { get; set; }
}
