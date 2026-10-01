using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Commands.AddTrack;

/// <summary>
/// Adds a track to a playlist — `POST /api/v1/playlists/{playlistId}/tracks`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="YoutubeVideoId">The video id, stored verbatim.</param>
/// <param name="AfterTrackId">Insert after this track. Null with <paramref name="AtHead"/> false means the
/// tail.</param>
/// <param name="AtHead">True to insert before everything, which is what `afterTrackId: null` means in the
/// contract.</param>
/// <param name="ExpectedVersion">The collection's `ETag`, from `If-Match`.</param>
public sealed record AddTrackCommand(
    Guid PlaylistId,
    string YoutubeVideoId,
    Guid? AfterTrackId,
    bool AtHead,
    uint ExpectedVersion) : IRequest<TrackWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.add-track";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
