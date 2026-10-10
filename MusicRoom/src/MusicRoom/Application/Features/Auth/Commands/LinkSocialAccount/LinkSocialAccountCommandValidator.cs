using FluentValidation;
using MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.LinkSocialAccount;

/// <summary>
/// The shape of a link request, which is the sign-in shape — SPEC-041.
/// </summary>
/// <remarks>
/// The same four fields and the same bounds, because linking uses the same acquisition: proving the caller
/// holds the provider identity is what stops anyone attaching their account to somebody else's.
/// </remarks>
public sealed class LinkSocialAccountCommandValidator : AbstractValidator<LinkSocialAccountCommand>
{
    /// <summary>Declares the rules.</summary>
    public LinkSocialAccountCommandValidator()
    {
        RuleFor(command => command.Provider)
            .NotEmpty().WithErrorCode(ValidationCodes.ProviderRequired);

        RuleFor(command => command.Code)
            .NotEmpty().WithErrorCode(ValidationCodes.CodeRequired)
            .MaximumLength(SignInWithSocialCommandValidator.SecretMaximumLength)
            .WithErrorCode(ValidationCodes.CodeRequired);

        RuleFor(command => command.CodeVerifier)
            .NotEmpty().WithErrorCode(ValidationCodes.CodeVerifierRequired)
            .MaximumLength(SignInWithSocialCommandValidator.SecretMaximumLength)
            .WithErrorCode(ValidationCodes.CodeVerifierRequired);

        RuleFor(command => command.RedirectUri)
            .NotEmpty().WithErrorCode(ValidationCodes.RedirectUriInvalid)
            .Must(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .WithErrorCode(ValidationCodes.RedirectUriInvalid);
    }
}
