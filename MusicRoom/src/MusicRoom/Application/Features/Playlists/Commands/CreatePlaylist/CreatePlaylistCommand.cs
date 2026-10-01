using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;

/// <summary>
/// Creates a playlist owned by the caller — `POST /api/v1/playlists`.
/// </summary>
/// <param name="Name">What it is called.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it. `LocationAndTimeRestricted` is `422` for a playlist.</param>
public sealed record CreatePlaylistCommand(string Name, Visibility Visibility, LicenceMode Licence)
    : IRequest<Guid>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "playlist.create";

    /// <inheritdoc />
    public string AuditTargetType => "playlist";
}
