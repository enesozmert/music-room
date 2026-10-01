using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playlists.Commands.RemoveTrack;

/// <summary>
/// Removes a track, if the caller may edit the playlist and still holds its version.
/// </summary>
internal sealed class RemoveTrackCommandHandler : IRequestHandler<RemoveTrackCommand, TrackWriteResult>
{
    private readonly IPlaylistRepository _playlists;
    private readonly IPlaylistTrackRepository _tracks;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the playlist gate, the track write path, the caller and the audit context.</summary>
    /// <param name="playlists">Where the `licence` gate is decided.</param>
    /// <param name="tracks">Where tracks live.</param>
    /// <param name="currentUser">Who is removing it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RemoveTrackCommandHandler(
        IPlaylistRepository playlists,
        IPlaylistTrackRepository tracks,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _playlists = playlists;
        _tracks = tracks;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Removes it, or reports why not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `204`, `412` or `404`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<TrackWriteResult> Handle(
        RemoveTrackCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RemoveTrackCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-015.");

        _audit.SetTargetId(request.PlaylistId);

        PlaylistAccess access = await _playlists
            .FindAccessAsync(request.PlaylistId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MayEdit)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return TrackWriteResult.NotFound;
        }

        TrackWriteResult result = await _tracks
            .TryRemoveAsync(
                request.PlaylistId,
                request.PlaylistTrackId,
                request.ExpectedVersion,
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome != TrackWriteOutcome.Written)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return result;
    }
}
