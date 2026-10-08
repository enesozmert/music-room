using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.RotateSession;

/// <summary>
/// Input rules for <see cref="RotateSessionCommand"/> — application-security.
/// </summary>
/// <remarks>
/// No message interpolates the submitted token. A validation response that quoted it back would put
/// a live credential in a log, a proxy and a crash report at once — and the endpoint answers a
/// bodiless <c>401</c> to a rule failure for the same reason.
/// </remarks>
public sealed class RotateSessionCommandValidator : AbstractValidator<RotateSessionCommand>
{
    /// <summary>
    /// The length the generator produces: 32 random bytes, base64url-encoded, which is 43
    /// characters. The bound is here so a caller cannot make the service hash or query a megabyte.
    /// </summary>
    public const int RefreshTokenLength = 43;

    /// <summary>Declares the rules.</summary>
    public RotateSessionCommandValidator() =>
        RuleFor(command => command.RefreshToken)
            .NotEmpty().WithErrorCode(ValidationCodes.RefreshTokenRequired)
            .MaximumLength(RefreshTokenLength).WithErrorCode(ValidationCodes.RefreshTokenTooLong);
}
