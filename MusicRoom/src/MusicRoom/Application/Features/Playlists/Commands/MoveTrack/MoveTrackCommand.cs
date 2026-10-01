using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Commands.MoveTrack;

/// <summary>
/// Moves a track — `PATCH /api/v1/playlists/{playlistId}/tracks/{playlistTrackId}`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="PlaylistTrackId">The track that moves.</param>
/// <param name="AfterTrackId">Put it after this one.</param>
/// <param name="BeforeTrackId">Put it before this one.</param>
/// <param name="ExpectedVersion">The collection's `ETag`, from `If-Match`.</param>
/// <remarks>
/// The write subject V.2.3 grades. Exactly one neighbour is required: both is `422` by the contract's own rule,
/// and neither is `422` because a move without a destination is not a move.
/// </remarks>
public sealed record MoveTrackCommand(
    Guid PlaylistId,
    Guid PlaylistTrackId,
    Guid? AfterTrackId,
    Guid? BeforeTrackId,
    uint ExpectedVersion) : IRequest<TrackWriteResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.move-track";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
