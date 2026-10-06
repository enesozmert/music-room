using FluentValidation;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Input rules for <see cref="RegisterCommand"/> — ADR-0007 and authentication-authorization.
/// </summary>
/// <remarks>
/// No message interpolates a submitted value. A validation response that quoted the password back
/// would put it in a log, a proxy and a crash report at once.
/// </remarks>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Declares the rules.</summary>
    public RegisterCommandValidator()
    {
        // The RFC 5321 path limit, and the length of the email column.
        const int emailMaximumLength = 320;

        const int displayNameMaximumLength = 64;

        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(emailMaximumLength)
            .EmailAddress();

        // The policy lives in PasswordRules — SPEC-034. Applied to this command's own property, so the `422`
        // body stays keyed on `Password`.
        RuleFor(command => command.Password).ChosenPassword();

        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(displayNameMaximumLength);
    }
}
