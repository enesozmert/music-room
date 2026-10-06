using FluentValidation;
using MusicRoom.Application.Features.Auth;

namespace MusicRoom.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Input rules for <see cref="ResetPasswordCommand"/> — ADR-0007 and authentication-authorization.
/// </summary>
/// <remarks>
/// The password rules are registration's, because the same BCrypt truncation applies to the same
/// column. No message interpolates a submitted value: one of these fields is a password and the other
/// is a token that replaces one.
/// </remarks>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>
    /// The length the generator produces: 32 random bytes, base64url-encoded, which is 43 characters.
    /// </summary>
    public const int TokenLength = 43;

    /// <summary>Declares the rules.</summary>
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Token)
            .NotEmpty()
            .MaximumLength(TokenLength);

        // The policy lives in PasswordRules — SPEC-034. Applied to this command's own property, so the `422`
        // body stays keyed on `NewPassword`.
        RuleFor(command => command.NewPassword).ChosenPassword();
    }
}
