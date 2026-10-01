using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;

/// <summary>
/// Writes a new playlist owned by the caller — subject V.2.3.
/// </summary>
internal sealed class CreatePlaylistCommandHandler : IRequestHandler<CreatePlaylistCommand, Guid>
{
    private readonly IPlaylistRepository _playlists;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the playlist write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where playlists live.</param>
    /// <param name="currentUser">Who is creating, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public CreatePlaylistCommandHandler(
        IPlaylistRepository playlists,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _playlists = playlists;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Creates it and returns its id.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The new playlist's id, which the endpoint puts in `Location`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<Guid> Handle(CreatePlaylistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "CreatePlaylistCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-014.");

        DateTime nowUtc = DateTime.UtcNow;

        Playlist playlist = new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Visibility = request.Visibility,
            Licence = request.Licence,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        await _playlists.CreateAsync(playlist, cancellationToken).ConfigureAwait(false);

        _audit.SetTargetId(playlist.Id);

        return playlist.Id;
    }
}
