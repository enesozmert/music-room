using FluentValidation;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Input rules for <see cref="VerifyEmailCommand"/> — application-security.
/// </summary>
/// <remarks>
/// No message interpolates the submitted token. A validation response that quoted it back would put
/// a live credential in a log, a proxy and a crash report at once.
/// </remarks>
public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    /// <summary>
    /// The length the generator produces: 32 random bytes, base64url-encoded, which is 43
    /// characters. The bound is here so a caller cannot make the service hash or query a megabyte.
    /// </summary>
    public const int TokenLength = 43;

    /// <summary>Declares the rules.</summary>
    public VerifyEmailCommandValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty()
            .MaximumLength(TokenLength);
}
