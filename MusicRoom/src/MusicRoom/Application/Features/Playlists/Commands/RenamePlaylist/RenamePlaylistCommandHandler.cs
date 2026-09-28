using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.RenamePlaylist;

/// <summary>
/// Renames a playlist, if the caller may edit it and still holds the current token.
/// </summary>
internal sealed class RenamePlaylistCommandHandler
    : IRequestHandler<RenamePlaylistCommand, PlaylistWriteOutcome>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is renaming, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RenamePlaylistCommandHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _playlists = playlists;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Renames it, or reports why not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Updated, a version mismatch, or not found — which covers "may not edit".</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaylistWriteOutcome> Handle(
        RenamePlaylistCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RenamePlaylistCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        _audit.SetTargetId(request.PlaylistId);

        PlaylistAccess access = await _playlists
            .FindAccessAsync(request.PlaylistId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MayEdit)
        {
            // A playlist the caller may not edit answers exactly as one that does not exist, so a caller cannot
            // map the catalogue by probing ids.
            _audit.SetOutcome(AuditOutcome.Refused);

            return PlaylistWriteOutcome.NotFound;
        }

        PlaylistWriteOutcome outcome = await _playlists
            .TryRenameAsync(
                request.PlaylistId,
                request.Name.Trim(),
                request.ExpectedVersion,
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome != PlaylistWriteOutcome.Updated)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }
}
