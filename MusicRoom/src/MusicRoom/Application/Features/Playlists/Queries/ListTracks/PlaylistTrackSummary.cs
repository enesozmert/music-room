namespace MusicRoom.Application.Features.Playlists.Queries.ListTracks;

/// <summary>
/// One track in a playlist, as a client reads it — `GET /api/v1/playlists/{playlistId}/tracks`.
/// </summary>
/// <param name="PlaylistTrackId">The id the move and remove routes take.</param>
/// <param name="YoutubeVideoId">The video id, verbatim. No title or duration: that is slice S4's catalog.</param>
/// <param name="Position">The sparse ordering key. Exposed because it is not a secret and a client that has it
/// can render a stable list without inferring order from array indices.</param>
/// <param name="AddedByUserId">Who added it.</param>
public sealed record PlaylistTrackSummary(
    Guid PlaylistTrackId,
    string YoutubeVideoId,
    long Position,
    Guid AddedByUserId);

/// <summary>
/// A page of tracks and the version of the collection they came from.
/// </summary>
/// <param name="Tracks">The page, ordered by position.</param>
/// <param name="Version">The playlist row's `xmin`, which the endpoint returns as the `ETag` and the client
/// sends back as `If-Match` when it writes.</param>
/// <remarks>
/// The version belongs to the **playlist**, not to a track: `If-Match` has to protect a collection, and a
/// collection has no row of its own — SPEC-015 decision 2.
/// </remarks>
public sealed record PlaylistTrackPage(IReadOnlyList<PlaylistTrackSummary> Tracks, uint Version);
