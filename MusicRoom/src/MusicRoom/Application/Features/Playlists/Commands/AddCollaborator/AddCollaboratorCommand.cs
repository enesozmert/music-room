using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Commands.AddCollaborator;

/// <summary>
/// Names somebody on a playlist — `POST /api/v1/playlists/{playlistId}/collaborators`. Owner only.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="UserId">The account to name.</param>
/// <param name="CanEdit">Whether they may write to it, not merely read it.</param>
public sealed record AddCollaboratorCommand(Guid PlaylistId, Guid UserId, bool CanEdit)
    : IRequest<AddCollaboratorOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.add-collaborator";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
