using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.RequestEmailVerification;

/// <summary>
/// Input rules for <see cref="RequestEmailVerificationCommand"/> — application-security.
/// </summary>
/// <remarks>
/// No message interpolates the submitted address. A validation response that quoted it back would
/// put it in a log, a proxy and a crash report at once.
/// </remarks>
public sealed class RequestEmailVerificationCommandValidator
    : AbstractValidator<RequestEmailVerificationCommand>
{
    /// <summary>
    /// The RFC 5321 path limit, and the length of the email column. The same bound
    /// <c>RegisterCommandValidator</c> applies, because an address this endpoint cannot match is an
    /// address registration could not have stored.
    /// </summary>
    public const int EmailMaximumLength = 320;

    /// <summary>Declares the rules.</summary>
    public RequestEmailVerificationCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.EmailRequired)
            .MaximumLength(EmailMaximumLength).WithErrorCode(ValidationCodes.EmailTooLong)
            .EmailAddress().WithErrorCode(ValidationCodes.EmailInvalid);
}
