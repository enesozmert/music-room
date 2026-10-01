using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playlists.Commands.SetPlaylistLicence;

/// <summary>
/// Sets a playlist's visibility and licence — `PUT /api/v1/playlists/{playlistId}/licence`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it.</param>
public sealed record SetPlaylistLicenceCommand(Guid PlaylistId, Visibility Visibility, LicenceMode Licence)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.set-licence";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
