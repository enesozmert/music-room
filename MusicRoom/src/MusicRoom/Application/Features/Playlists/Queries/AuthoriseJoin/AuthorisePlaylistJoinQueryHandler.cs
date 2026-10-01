using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Queries.AuthoriseJoin;

/// <summary>
/// Answers the `member` gate for a hub join — the same gate the REST read uses.
/// </summary>
internal sealed class AuthorisePlaylistJoinQueryHandler : IRequestHandler<AuthorisePlaylistJoinQuery, bool>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the playlist gate and the caller.</summary>
    /// <param name="playlists">Where the `member` gate is decided.</param>
    /// <param name="currentUser">Who is asking — on this transport, from the hub's own principal.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AuthorisePlaylistJoinQueryHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser)
    {
        _playlists = playlists;
        _currentUser = currentUser;
    }

    /// <summary>True when the caller may see the playlist, and so may hear about it.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The connection's cancellation token.</param>
    /// <returns>Whether the join is allowed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(AuthorisePlaylistJoinQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "AuthorisePlaylistJoinQuery reached its handler with no authenticated caller. The hub "
                + "requires authorisation — SPEC-016.");

        PlaylistAccess access = await _playlists
            .FindAccessAsync(request.PlaylistId, userId, cancellationToken)
            .ConfigureAwait(false);

        // Seeing it is enough to hear about it. Writing is a separate gate, enforced on the REST route the
        // write actually goes through — the hub carries no mutations.
        return access.MaySee;
    }
}
