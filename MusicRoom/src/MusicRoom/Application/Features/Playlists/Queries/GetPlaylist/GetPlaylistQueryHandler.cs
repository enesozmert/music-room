using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Queries.GetPlaylist;

/// <summary>
/// Reads a playlist the caller may see, with the concurrency token the `ETag` carries.
/// </summary>
internal sealed class GetPlaylistQueryHandler : IRequestHandler<GetPlaylistQuery, PlaylistDetail?>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the playlist read path and the caller.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetPlaylistQueryHandler(IPlaylistRepository playlists, ICurrentUserAccessor currentUser)
    {
        _playlists = playlists;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The playlist, or null when the caller may not see it.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<PlaylistDetail?> Handle(GetPlaylistQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetPlaylistQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        return _playlists.FindAsync(request.PlaylistId, userId, cancellationToken);
    }
}
