using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.UnlinkSocialAccount;

/// <summary>
/// The shape of an unlink request — SPEC-041.
/// </summary>
public sealed class UnlinkSocialAccountCommandValidator : AbstractValidator<UnlinkSocialAccountCommand>
{
    /// <summary>Declares the rules.</summary>
    public UnlinkSocialAccountCommandValidator() =>
        RuleFor(command => command.Provider)
            .NotEmpty().WithErrorCode(ValidationCodes.ProviderRequired);
}
