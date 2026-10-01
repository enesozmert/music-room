using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.DeletePlaylist;

/// <summary>
/// Stamps a playlist deleted, if the caller owns it.
/// </summary>
internal sealed class DeletePlaylistCommandHandler : IRequestHandler<DeletePlaylistCommand, bool>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is deleting, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public DeletePlaylistCommandHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _playlists = playlists;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Deletes it, if the caller owns it.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the owner's playlist was deleted.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(DeletePlaylistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "DeletePlaylistCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        _audit.SetTargetId(request.PlaylistId);

        bool deleted = await _playlists
            .TryDeleteAsync(request.PlaylistId, ownerId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return deleted;
    }
}
