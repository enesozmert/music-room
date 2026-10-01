using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Queries.ListTracks;

/// <summary>
/// Reads a page of tracks for a caller who may see the playlist — the `member` gate.
/// </summary>
internal sealed class ListTracksQueryHandler : IRequestHandler<ListTracksQuery, PlaylistTrackPage?>
{
    private readonly IPlaylistRepository _playlists;
    private readonly IPlaylistTrackRepository _tracks;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the playlist gate, the track read path and the caller.</summary>
    /// <param name="playlists">Where the `member` gate is decided.</param>
    /// <param name="tracks">Where tracks live.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListTracksQueryHandler(
        IPlaylistRepository playlists,
        IPlaylistTrackRepository tracks,
        ICurrentUserAccessor currentUser)
    {
        _playlists = playlists;
        _tracks = tracks;
        _currentUser = currentUser;
    }

    /// <summary>Lists them, or answers null when the caller may not see the playlist.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page and the collection's version, or null.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaylistTrackPage?> Handle(
        ListTracksQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListTracksQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-015.");

        PlaylistAccess access = await _playlists
            .FindAccessAsync(request.PlaylistId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            return null;
        }

        return await _tracks
            .ListAsync(request.PlaylistId, request.Page, request.PageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
