using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Commands.RemoveTrack;

/// <summary>
/// Removes a track — `DELETE /api/v1/playlists/{playlistId}/tracks/{playlistTrackId}`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="PlaylistTrackId">The track to remove.</param>
/// <param name="ExpectedVersion">The collection's `ETag`, from `If-Match`.</param>
public sealed record RemoveTrackCommand(Guid PlaylistId, Guid PlaylistTrackId, uint ExpectedVersion)
    : IRequest<TrackWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.remove-track";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
