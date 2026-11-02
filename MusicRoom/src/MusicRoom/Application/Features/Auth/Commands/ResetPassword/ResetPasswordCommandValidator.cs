using FluentValidation;
using MusicRoom.Application.Validation;
using MusicRoom.Application.Abstractions.Security;
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

    /// <summary>
    /// The longest code this endpoint reads before giving up, with room for the spaces and dashes a
    /// person adds when copying six characters out of a mail client — SPEC-055.
    /// </summary>
    public const int CodeLength = 32;

    /// <summary>The longest address, matching the column — the same bound registration uses.</summary>
    public const int EmailLength = 320;

    /// <summary>Declares the rules.</summary>
    /// <param name="breachedPasswords">The bundled breached set, resolved by the container — SPEC-035.</param>
    /// <remarks>
    /// The context screen is not here: this command carries a token, not an account. Its handler reads the
    /// account's words before the write — SPEC-035.
    /// </remarks>
    public ResetPasswordCommandValidator(IBreachedPasswordSet breachedPasswords)
    {
        // One credential or the other — SPEC-055. Neither is `NotEmpty` on its own, because either one
        // alone is a complete request and insisting on both would refuse every link in existence.
        RuleFor(command => command)
            .Must(command => command.Token is { Length: > 0 } || command.Code is { Length: > 0 })
            .WithErrorCode(ValidationCodes.TokenRequired)
            // camelCase, spelled out: `OverridePropertyName` names a key and bypasses the global
            // resolver that would otherwise lower-case it — ADR-0043.
            .OverridePropertyName("token");

        RuleFor(command => command.Token)
            .MaximumLength(TokenLength).WithErrorCode(ValidationCodes.TokenTooLong)
            .When(command => command.Token is not null);

        RuleFor(command => command.Code)
            .MaximumLength(CodeLength).WithErrorCode(ValidationCodes.TokenTooLong)
            .When(command => command.Code is not null);

        // The address scopes the code: six characters are not unique across accounts, so the code path
        // needs to know whose row to look at — SPEC-055 decision 4.
        RuleFor(command => command.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.EmailRequired)
            .MaximumLength(EmailLength).WithErrorCode(ValidationCodes.EmailTooLong)
            .When(command => command.Code is { Length: > 0 });

        // The policy lives in PasswordRules — SPEC-034. Applied to this command's own property, so the `422`
        // body stays keyed on `NewPassword`.
        RuleFor(command => command.NewPassword).ChosenPassword(breachedPasswords);
    }
}
