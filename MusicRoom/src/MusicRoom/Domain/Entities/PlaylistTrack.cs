using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One track in a playlist, at one position — subject V.2.3.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is a **sparse** key with a nominal gap of 1000, so inserting between two tracks
/// writes one row instead of rewriting the tail. When two neighbours have no gap left between them the whole
/// playlist is renumbered, which is bounded by the 500-track cap.
/// <para>
/// <see cref="YoutubeVideoId"/> is stored as the client sent it. Resolving it to a title or a duration is the
/// catalog of slice S4 (ADR-0023) and is not this entity's business.
/// </para>
/// </remarks>
public sealed class PlaylistTrack : Entity
{
    /// <summary>The nominal distance between neighbouring positions.</summary>
    public const long PositionGap = 1000;

    /// <summary>How many tracks one playlist may hold.</summary>
    public const int MaximumTracksPerPlaylist = 500;

    /// <summary>The playlist this belongs to.</summary>
    public Guid PlaylistId { get; set; }

    /// <summary>The YouTube video id, verbatim.</summary>
    public required string YoutubeVideoId { get; set; }

    /// <summary>
    /// Where it sits. Sparse: the gap between neighbours is what lets an insert write one row.
    /// </summary>
    public long Position { get; set; }

    /// <summary>Who added it.</summary>
    public Guid AddedByUserId { get; set; }
}
