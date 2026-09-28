using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Queries.ListPlaylists;

/// <summary>
/// Lists the playlists the caller may see in one scope.
/// </summary>
internal sealed class ListPlaylistsQueryHandler
    : IRequestHandler<ListPlaylistsQuery, IReadOnlyList<PlaylistSummary>>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the playlist read path and the caller.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is listing, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListPlaylistsQueryHandler(IPlaylistRepository playlists, ICurrentUserAccessor currentUser)
    {
        _playlists = playlists;
        _currentUser = currentUser;
    }

    /// <summary>Lists them.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per playlist in that scope.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<IReadOnlyList<PlaylistSummary>> Handle(
        ListPlaylistsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListPlaylistsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        return _playlists.ListAsync(userId, request.Scope, request.Page, request.PageSize, cancellationToken);
    }
}
