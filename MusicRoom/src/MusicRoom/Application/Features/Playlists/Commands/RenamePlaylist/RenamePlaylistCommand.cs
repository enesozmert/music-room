using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Commands.RenamePlaylist;

/// <summary>
/// Changes a playlist's metadata under `If-Match` — `PATCH /api/v1/playlists/{playlistId}`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Name">The new name.</param>
/// <param name="ExpectedVersion">The concurrency token the caller last read, from `If-Match`.</param>
/// <remarks>
/// Guarded rather than last-write-wins: two clients renaming at once is the same class of failure as two
/// reordering at once, and the contract answers `412` to both.
/// </remarks>
public sealed record RenamePlaylistCommand(Guid PlaylistId, string Name, uint ExpectedVersion)
    : IRequest<PlaylistWriteOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.rename";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
