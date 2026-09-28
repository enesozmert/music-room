using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.SetPlaylistLicence;

/// <summary>
/// Sets visibility and licence together. Owner only — a collaborator with `canEdit` may change contents, not
/// who may see them.
/// </summary>
internal sealed class SetPlaylistLicenceCommandHandler : IRequestHandler<SetPlaylistLicenceCommand, bool>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is setting it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SetPlaylistLicenceCommandHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _playlists = playlists;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Sets them, if the caller owns the playlist.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the owner's playlist was updated.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(SetPlaylistLicenceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "SetPlaylistLicenceCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        _audit.SetTargetId(request.PlaylistId);

        bool updated = await _playlists
            .TrySetLicenceAsync(
                request.PlaylistId,
                ownerId,
                new PlaylistLicence(request.Visibility, request.Licence),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return updated;
    }
}
