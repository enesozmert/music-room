using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Playlists.Commands.RemoveCollaborator;

/// <summary>
/// Removes somebody from a playlist — `DELETE /api/v1/playlists/{playlistId}/collaborators/{userId}`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="UserId">The account to remove.</param>
public sealed record RemoveCollaboratorCommand(Guid PlaylistId, Guid UserId)
    : IRequest<bool>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.remove-collaborator";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
