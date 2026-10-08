using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.Login;

/// <summary>
/// Input rules for <see cref="LoginCommand"/> — application-security.
/// </summary>
/// <remarks>
/// The bounds are here so an anonymous caller cannot make the service hash or query a megabyte. They
/// are deliberately looser than <c>RegisterCommandValidator</c>'s: this endpoint judges a stored
/// hash, not a new password, and a minimum length here would tell a caller which candidates are too
/// short to be anybody's password. Every failure leaves as a bodiless <c>401</c> anyway.
/// </remarks>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    /// <summary>The RFC 5321 path limit, and the length of the <c>email</c> column.</summary>
    public const int EmailMaximumLength = 320;

    /// <summary>The upper bound registration accepts, so nothing longer can be a stored password.</summary>
    public const int PasswordMaximumLength = 64;

    /// <summary>Declares the rules.</summary>
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.EmailRequired)
            .MaximumLength(EmailMaximumLength).WithErrorCode(ValidationCodes.EmailTooLong);

        RuleFor(command => command.Password)
            .NotEmpty().WithErrorCode(ValidationCodes.PasswordRequired)
            .MaximumLength(PasswordMaximumLength).WithErrorCode(ValidationCodes.PasswordTooLong);
    }
}
