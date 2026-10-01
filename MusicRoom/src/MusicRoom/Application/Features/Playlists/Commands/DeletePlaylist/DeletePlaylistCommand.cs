using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Playlists.Commands.DeletePlaylist;

/// <summary>
/// Soft-deletes a playlist — `DELETE /api/v1/playlists/{playlistId}`. Owner only.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <remarks>
/// Soft: the row stays, stamped `deleted_at_utc`, and a global query filter hides it from every read. A hard
/// delete would take its collaborators and, later, its tracks with it and leave the audit trail pointing at
/// nothing.
/// </remarks>
public sealed record DeletePlaylistCommand(Guid PlaylistId) : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.delete";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
