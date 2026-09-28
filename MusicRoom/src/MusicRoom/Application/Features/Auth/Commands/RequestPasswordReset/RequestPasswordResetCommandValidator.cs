using FluentValidation;

namespace MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Input rules for <see cref="RequestPasswordResetCommand"/> — application-security.
/// </summary>
/// <remarks>
/// The rules describe the shape of an address, which is public knowledge, and say nothing about which
/// addresses exist. The endpoint answers <c>202</c> for every address that passes them.
/// </remarks>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    /// <summary>The RFC 5321 path limit, and the length of the <c>email</c> column.</summary>
    public const int EmailMaximumLength = 320;

    /// <summary>Declares the rules.</summary>
    public RequestPasswordResetCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(EmailMaximumLength)
            .EmailAddress();
}
