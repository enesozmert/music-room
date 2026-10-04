using System.Text;
using FluentValidation;

namespace MusicRoom.Application.Features.Auth.Commands.ChangePassword;

/// <summary>
/// Input rules for <see cref="ChangePasswordCommand"/> — ADR-0007, `authentication-authorization`.
/// </summary>
/// <remarks>
/// The new password is held to registration's rules, because the same BCrypt truncation applies to the same
/// column. The **current** password is checked for presence only: it is verified against the stored hash by the
/// handler, and a length rule on it would refuse a caller whose existing password predates a policy change
/// before the hash was even consulted.
/// <para>
/// No message interpolates a submitted value — both fields here are passwords.
/// </para>
/// </remarks>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    /// <summary>BCrypt truncates its input here, silently, so the limit is rejected in validation.</summary>
    public const int PasswordMaximumUtf8Bytes = 72;

    /// <summary>Declares the rules.</summary>
    public ChangePasswordCommandValidator()
    {
        // NIST SP 800-63B asks verifiers to accept at least 64 characters; the upper bound is what keeps every
        // accepted password inside BCrypt's input window.
        const int passwordMinimumLength = 12;
        const int passwordMaximumLength = 64;

        RuleFor(command => command.CurrentPassword)
            .NotEmpty();

        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .MinimumLength(passwordMinimumLength)
            .WithMessage($"Password must be at least {passwordMinimumLength} characters.")
            .MaximumLength(passwordMaximumLength)
            .WithMessage($"Password must be at most {passwordMaximumLength} characters.")
            .Must(FitsInBCryptInput)
            .WithMessage($"Password must be at most {PasswordMaximumUtf8Bytes} bytes when encoded as UTF-8.");

        // A change that changes nothing is refused here rather than written: it would revoke every other
        // session for no reason, which is a surprising amount of damage for a no-op.
        RuleFor(command => command.NewPassword)
            .Must((command, newPassword) =>
                !string.Equals(newPassword, command.CurrentPassword, StringComparison.Ordinal))
            .WithMessage("The new password must differ from the current one.");
    }

    private static bool FitsInBCryptInput(string? password) =>
        password is null || Encoding.UTF8.GetByteCount(password) <= PasswordMaximumUtf8Bytes;
}
