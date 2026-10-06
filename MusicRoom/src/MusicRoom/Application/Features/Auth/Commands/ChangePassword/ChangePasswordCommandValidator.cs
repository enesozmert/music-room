using FluentValidation;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Auth;

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
    /// <summary>Declares the rules.</summary>
    /// <param name="breachedPasswords">The bundled breached set, resolved by the container — SPEC-035.</param>
    /// <remarks>
    /// The context screen is not here: this command carries two passwords and no account. Its handler already
    /// loads the row and screens from it — SPEC-035.
    /// </remarks>
    public ChangePasswordCommandValidator(IBreachedPasswordSet breachedPasswords)
    {
        RuleFor(command => command.CurrentPassword)
            .NotEmpty();

        // The policy lives in PasswordRules — SPEC-034. Applied to this command's own property, so the `422`
        // body stays keyed on `NewPassword`.
        RuleFor(command => command.NewPassword).ChosenPassword(breachedPasswords);

        // A change that changes nothing is refused here rather than written: it would revoke every other
        // session for no reason, which is a surprising amount of damage for a no-op.
        RuleFor(command => command.NewPassword)
            .Must((command, newPassword) =>
                !string.Equals(newPassword, command.CurrentPassword, StringComparison.Ordinal))
            .WithMessage("The new password must differ from the current one.");
    }
}
