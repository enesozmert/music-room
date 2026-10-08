using FluentValidation;
using MusicRoom.Application.Validation;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Input rules for <see cref="RegisterCommand"/> — ADR-0007 and authentication-authorization.
/// </summary>
/// <remarks>
/// No message interpolates a submitted value. A validation response that quoted the password back
/// would put it in a log, a proxy and a crash report at once.
/// <para>
/// This is the one path whose command carries the context words, so its context screen is a rule here rather
/// than a check in a handler — SPEC-035, ADR-0042 as corrected. The other two paths cannot do this: reset
/// carries a token and change carries two passwords.
/// </para>
/// </remarks>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Declares the rules.</summary>
    /// <param name="breachedPasswords">The bundled breached set, resolved by the container — SPEC-035.</param>
    public RegisterCommandValidator(IBreachedPasswordSet breachedPasswords)
    {
        // The RFC 5321 path limit, and the length of the email column.
        const int emailMaximumLength = 320;

        const int displayNameMaximumLength = 64;

        RuleFor(command => command.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.EmailRequired)
            .MaximumLength(emailMaximumLength).WithErrorCode(ValidationCodes.EmailTooLong)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid);

        // The policy lives in PasswordRules — SPEC-034. Applied to this command's own property, so the `422`
        // body stays keyed on `Password`.
        RuleFor(command => command.Password).ChosenPassword(breachedPasswords);

        // The context screen, on the same property for the same reason: a client reads `errors.Password`.
        // `command` rather than the property alone, because the words are the address and the display name this
        // very request submitted — V6.1.2, V6.2.11.
        RuleFor(command => command.Password)
            .Must((command, password) =>
                !PasswordContext.ContainsAContextWord(password, command.Email, command.DisplayName))
            .WithMessage(PasswordRules.ContextMessage)
            .WithErrorCode(ValidationCodes.PasswordContextWord);

        RuleFor(command => command.DisplayName)
            .NotEmpty().WithErrorCode(ValidationCodes.DisplayNameRequired)
            .MaximumLength(displayNameMaximumLength).WithErrorCode(ValidationCodes.DisplayNameTooLong);
    }
}
