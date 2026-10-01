using FluentValidation;
using MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;

namespace MusicRoom.Application.Features.Playlists.Commands.RenamePlaylist;

/// <summary>
/// Input rules for <see cref="RenamePlaylistCommand"/> — the same name bound creation enforces.
/// </summary>
public sealed class RenamePlaylistCommandValidator : AbstractValidator<RenamePlaylistCommand>
{
    /// <summary>Declares the rules.</summary>
    public RenamePlaylistCommandValidator()
    {
        RuleFor(command => command.PlaylistId).NotEmpty();

        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(CreatePlaylistCommandValidator.NameMaximumLength);
    }
}
