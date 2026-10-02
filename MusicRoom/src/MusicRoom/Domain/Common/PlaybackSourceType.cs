namespace MusicRoom.Domain.Common;

/// <summary>
/// What a playback session is playing from — ADR-0017.
/// </summary>
/// <remarks>
/// The source travels as a `(source_type, source_id)` pair with no foreign key, because one column pair for two
/// possible parents is the honest shape: two nullable foreign keys would allow a row that names both or neither.
/// The cost is stated in SPEC-021 decision 3 — nothing in the database stops a session naming a deleted source,
/// so the read resolves it and an advance treats a vanished source as an exhausted queue.
/// </remarks>
public enum PlaybackSourceType
{
    /// <summary>A track-vote event, whose next item is the highest vote count — subject V.2.1.</summary>
    Event = 0,

    /// <summary>A playlist, whose next item is the next position — subject V.2.3.</summary>
    Playlist = 1,
}
