using FluentValidation;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playlists.Commands.SetPlaylistLicence;

/// <summary>
/// Input rules for <see cref="SetPlaylistLicenceCommand"/>.
/// </summary>
public sealed class SetPlaylistLicenceCommandValidator : AbstractValidator<SetPlaylistLicenceCommand>
{
    /// <summary>Declares the rules.</summary>
    public SetPlaylistLicenceCommandValidator()
    {
        RuleFor(command => command.PlaylistId).NotEmpty();
        RuleFor(command => command.Visibility).IsInEnum();

        RuleFor(command => command.Licence)
            .IsInEnum()
            .NotEqual(LicenceMode.LocationAndTimeRestricted)
            .WithMessage("A playlist cannot be location-and-time restricted; that licence belongs to an event.");
    }
}
