using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.AddCollaborator;

/// <summary>
/// Adds a collaborator row, if the caller owns the playlist.
/// </summary>
internal sealed class AddCollaboratorCommandHandler
    : IRequestHandler<AddCollaboratorCommand, AddCollaboratorOutcome>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IPlaylistBroadcastQueue _broadcasts;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is naming them, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the other editors are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AddCollaboratorCommandHandler(
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

    /// <summary>Names them, or reports the conflict.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict, or no such playlist for this owner.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<AddCollaboratorOutcome> Handle(
        AddCollaboratorCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "AddCollaboratorCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        _audit.SetTargetId(request.PlaylistId);

        AddCollaboratorOutcome outcome = await _playlists
            .TryAddCollaboratorAsync(
                request.PlaylistId,
                ownerId,
                new CollaboratorGrant(request.UserId, request.CanEdit),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome != AddCollaboratorOutcome.Added)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return outcome;
        }

        // A collaborator row does not touch the playlist, so this reports the version the playlist already
        // has — a listener comparing it learns the track list did not change, which is true.
        _broadcasts.Enqueue(new CollaboratorChanged(
            request.PlaylistId,
            await _playlists.FindVersionAsync(request.PlaylistId, cancellationToken).ConfigureAwait(false),
            request.UserId,
            Removed: false));

        return outcome;
    }
}
