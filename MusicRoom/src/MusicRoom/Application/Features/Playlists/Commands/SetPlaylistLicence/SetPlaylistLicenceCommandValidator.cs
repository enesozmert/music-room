using FluentValidation;
using MusicRoom.Application.Validation;
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
        RuleFor(command => command.PlaylistId).NotEmpty().WithErrorCode(ValidationCodes.PlaylistRequired);
        RuleFor(command => command.Visibility).IsInEnum().WithErrorCode(ValidationCodes.VisibilityUnknown);

        RuleFor(command => command.Licence)
            .IsInEnum().WithErrorCode(ValidationCodes.LicenceUnknown)
            .NotEqual(LicenceMode.LocationAndTimeRestricted).WithErrorCode(ValidationCodes.LicenceNotAllowed)
            .WithMessage("A playlist cannot be location-and-time restricted; that licence belongs to an event.");
    }
}
