namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// Where a playlist's live events go — SDD §9.2, ADR-0035.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>WebAPI</c>, which is where SignalR lives. This layer knows that
/// something must be told, and nothing about hubs, groups or connections.
/// <para>
/// An implementation **must not throw.** The database is the arbiter: by the time this is called the action
/// has committed, and a notification that could not be delivered must not turn a completed action into a
/// `500`. An implementation logs its own failure and returns — ADR-0035.
/// </para>
/// </remarks>
public interface IPlaylistBroadcaster
{
    /// <summary>Sends one fact to everybody listening to the playlist it names.</summary>
    /// <param name="broadcast">What happened, and the version it happened at.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the send has been attempted.</returns>
    Task SendAsync(PlaylistBroadcast broadcast, CancellationToken cancellationToken);
}

/// <summary>
/// One fact about one playlist, at one version.
/// </summary>
/// <param name="PlaylistId">The playlist the listeners are grouped by.</param>
/// <param name="Version">
/// The playlist's `xmin` after the change — the same token the REST `ETag` carries. A client that receives
/// a version it did not expect knows it missed a message and resynchronises over REST, which is why every
/// payload carries one (`signalr-realtime`).
/// </param>
public abstract record PlaylistBroadcast(Guid PlaylistId, uint Version);

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
