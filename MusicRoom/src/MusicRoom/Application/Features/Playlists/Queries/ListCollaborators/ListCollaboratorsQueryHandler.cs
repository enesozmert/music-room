using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Queries.ListCollaborators;

/// <summary>
/// Lists collaborators to a caller who may see the playlist — the `member` gate on a read.
/// </summary>
internal sealed class ListCollaboratorsQueryHandler
    : IRequestHandler<ListCollaboratorsQuery, IReadOnlyList<CollaboratorSummary>>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the playlist read path and the caller.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is listing, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListCollaboratorsQueryHandler(IPlaylistRepository playlists, ICurrentUserAccessor currentUser)
    {
        _playlists = playlists;
        _currentUser = currentUser;
    }

    /// <summary>Lists them, or nothing when the caller may not see the playlist.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per collaborator.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<IReadOnlyList<CollaboratorSummary>> Handle(
        ListCollaboratorsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListCollaboratorsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        return _playlists.ListCollaboratorsAsync(request.PlaylistId, userId, cancellationToken);
    }
}
