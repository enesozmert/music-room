using System.Text;
using FluentValidation;

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

    /// <summary>BCrypt truncates its input here, silently, so the limit is rejected in validation.</summary>
    public const int PasswordMaximumUtf8Bytes = 72;

    /// <summary>Declares the rules.</summary>
    public ResetPasswordCommandValidator()
    {
        // NIST SP 800-63B asks verifiers to accept at least 64 characters; the upper bound is what
        // keeps every accepted password inside BCrypt's input window.
        const int passwordMinimumLength = 12;
        const int passwordMaximumLength = 64;

        RuleFor(command => command.Token)
            .NotEmpty()
            .MaximumLength(TokenLength);

        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .MinimumLength(passwordMinimumLength)
            .WithMessage($"Password must be at least {passwordMinimumLength} characters.")
            .MaximumLength(passwordMaximumLength)
            .WithMessage($"Password must be at most {passwordMaximumLength} characters.")
            .Must(FitsInBCryptInput)
            .WithMessage($"Password must be at most {PasswordMaximumUtf8Bytes} bytes when encoded as UTF-8.");
    }

    private static bool FitsInBCryptInput(string password) =>
        password is not null && Encoding.UTF8.GetByteCount(password) <= PasswordMaximumUtf8Bytes;
}
