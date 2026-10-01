namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// One fact about one playlist, at one version.
/// </summary>
/// <remarks>
/// A playlist has a parent row and every track write touches it, so the literal version rule of
/// `signalr-realtime` holds here: the token in the payload is the one the REST `ETag` carries, and a client
/// that sees an unexpected one knows it missed a message — ADR-0035.
/// </remarks>
/// <param name="PlaylistId">The playlist the listeners are grouped by.</param>
/// <param name="Version">
/// The playlist's `xmin` after the change — the same token the REST `ETag` carries. A client that receives
/// a version it did not expect knows it missed a message and resynchronises over REST, which is why every
/// payload carries one (`signalr-realtime`).
/// </param>
public abstract record PlaylistBroadcast(Guid PlaylistId, uint Version) : IRealtimeBroadcast
{
    /// <inheritdoc />
    public Guid AggregateId => PlaylistId;
}

/// <summary>A track was added — `TrackAdded`.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Version">The version after the add.</param>
/// <param name="PlaylistTrackId">The new track.</param>
/// <param name="YoutubeVideoId">The video, so a client can render without a second call.</param>
public sealed record TrackAdded(
    Guid PlaylistId,
    uint Version,
    Guid PlaylistTrackId,
    string YoutubeVideoId) : PlaylistBroadcast(PlaylistId, Version);

/// <summary>A track moved — `TrackMoved`.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Version">The version after the move.</param>
/// <param name="PlaylistTrackId">The track that moved.</param>
/// <remarks>
/// The new position is deliberately absent. A reorder is a property of the whole list, and a client that
/// re-reads the page gets an order it can trust; a position in a payload invites a client to patch its own
/// copy and drift from the server.
/// </remarks>
public sealed record TrackMoved(
    Guid PlaylistId,
    uint Version,
    Guid PlaylistTrackId) : PlaylistBroadcast(PlaylistId, Version);

/// <summary>A track was removed — `TrackRemoved`.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Version">The version after the removal.</param>
/// <param name="PlaylistTrackId">The track that is gone.</param>
public sealed record TrackRemoved(
    Guid PlaylistId,
    uint Version,
    Guid PlaylistTrackId) : PlaylistBroadcast(PlaylistId, Version);

/// <summary>Somebody was named on the playlist, or taken off it — `CollaboratorChanged`.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Version">The version after the change.</param>
/// <param name="UserId">Who was named or removed.</param>
/// <param name="Removed">True when they were taken off, false when they were named or regranted.</param>
/// <remarks>
/// `canEdit` is not in the payload: whether a listener may write is a question the listener asks about
/// itself, over REST, and broadcasting one person's grant to everybody in the group would tell the group
/// more than any of them needs.
/// </remarks>
public sealed record CollaboratorChanged(
    Guid PlaylistId,
    uint Version,
    Guid UserId,
    bool Removed) : PlaylistBroadcast(PlaylistId, Version);
