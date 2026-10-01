using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.RemoveCollaborator;

/// <summary>
/// Deletes a collaborator row, if the caller owns the playlist.
/// </summary>
internal sealed class RemoveCollaboratorCommandHandler : IRequestHandler<RemoveCollaboratorCommand, bool>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IPlaylistBroadcastQueue _broadcasts;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is removing them, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other editors are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RemoveCollaboratorCommandHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IPlaylistBroadcastQueue broadcasts)
    {
        _playlists = playlists;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Removes them, if there is such a row on the owner's playlist.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a collaborator was removed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(RemoveCollaboratorCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RemoveCollaboratorCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        _audit.SetTargetId(request.PlaylistId);

        bool removed = await _playlists
            .TryRemoveCollaboratorAsync(request.PlaylistId, ownerId, request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (!removed)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return removed;
        }

        _broadcasts.Enqueue(new CollaboratorChanged(
            request.PlaylistId,
            await _playlists.FindVersionAsync(request.PlaylistId, cancellationToken).ConfigureAwait(false),
            request.UserId,
            Removed: true));

        return removed;
    }
}
