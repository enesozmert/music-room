using System.Text;
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
    /// <summary>
    /// BCrypt truncates its input here, silently. Two long passwords would otherwise hash
    /// identically, so the limit is rejected at validation instead.
    /// </summary>
    internal const int PasswordMaximumUtf8Bytes = 72;

    /// <summary>Declares the rules.</summary>
    public RegisterCommandValidator()
    {
        // The RFC 5321 path limit, and the length of the email column.
        const int emailMaximumLength = 320;

        // NIST SP 800-63B asks verifiers to accept at least 64 characters; the upper bound is what
        // keeps every accepted password inside BCrypt's input window.
        const int passwordMinimumLength = 12;
        const int passwordMaximumLength = 64;
        const int displayNameMaximumLength = 64;

        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(emailMaximumLength)
            .EmailAddress();

        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(passwordMinimumLength)
            .WithMessage($"Password must be at least {passwordMinimumLength} characters.")
            .MaximumLength(passwordMaximumLength)
            .WithMessage($"Password must be at most {passwordMaximumLength} characters.")
            .Must(FitsInBCryptInput)
            .WithMessage($"Password must be at most {PasswordMaximumUtf8Bytes} bytes when encoded as UTF-8.");

        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(displayNameMaximumLength);
    }

    private static bool FitsInBCryptInput(string password) =>
        password is not null && Encoding.UTF8.GetByteCount(password) <= PasswordMaximumUtf8Bytes;
}
