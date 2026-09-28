using FluentValidation;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;

/// <summary>
/// Input rules for <see cref="CreatePlaylistCommand"/>.
/// </summary>
/// <remarks>
/// The licence rule is the one that matters: a playlist may not be `LocationAndTimeRestricted`, because subject
/// V.2.1 attaches location-and-time to an event and nothing checks a geofence on a playlist. Accepting the value
/// would be a licence the server silently ignores.
/// </remarks>
public sealed class CreatePlaylistCommandValidator : AbstractValidator<CreatePlaylistCommand>
{
    /// <summary>The column width for a playlist name.</summary>
    public const int NameMaximumLength = 120;

    /// <summary>Declares the rules.</summary>
    public CreatePlaylistCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(NameMaximumLength);

        RuleFor(command => command.Visibility).IsInEnum();

        RuleFor(command => command.Licence)
            .IsInEnum()
            .NotEqual(LicenceMode.LocationAndTimeRestricted)
            .WithMessage("A playlist cannot be location-and-time restricted; that licence belongs to an event.");
    }
}
