using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;

/// <summary>
/// The shape of a social sign-in request — SPEC-041.
/// </summary>
/// <remarks>
/// Length bounds rather than format checks on the code and the verifier: both are opaque strings a provider
/// issued, and a regular expression over somebody else's format is a guess that breaks when they change it.
/// What matters here is that neither is empty and neither is large enough to be an attack on the parser.
/// </remarks>
public sealed class SignInWithSocialCommandValidator : AbstractValidator<SignInWithSocialCommand>
{
    /// <summary>The longest code or verifier this API will carry to a provider.</summary>
    internal const int SecretMaximumLength = 2048;

    /// <summary>Declares the rules.</summary>
    public SignInWithSocialCommandValidator()
    {
        RuleFor(command => command.Provider)
            .NotEmpty().WithErrorCode(ValidationCodes.ProviderRequired);

        RuleFor(command => command.Code)
            .NotEmpty().WithErrorCode(ValidationCodes.CodeRequired)
            .MaximumLength(SecretMaximumLength).WithErrorCode(ValidationCodes.CodeRequired);

        RuleFor(command => command.CodeVerifier)
            .NotEmpty().WithErrorCode(ValidationCodes.CodeVerifierRequired)
            .MaximumLength(SecretMaximumLength).WithErrorCode(ValidationCodes.CodeVerifierRequired);

        // Absolute, because it is sent to a provider that compares it to what it registered. A relative
        // string would be refused there, which is a round trip to learn something checkable here.
        RuleFor(command => command.RedirectUri)
            .NotEmpty().WithErrorCode(ValidationCodes.RedirectUriInvalid)
            .Must(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .WithErrorCode(ValidationCodes.RedirectUriInvalid);
    }
}
