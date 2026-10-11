using FluentValidation;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Input rules for <see cref="VerifyEmailCommand"/> — application-security.
/// </summary>
/// <remarks>
/// No message interpolates the submitted credential. A validation response that quoted one back would put
/// it in a log, a proxy and a crash report at once.
/// <para>
/// **The rules bound a size, they do not decide an outcome** — SPEC-052. Whether a credential is the right
/// one is the handler's business and answers `400` with no body either way; what is refused here is a
/// megabyte of "token", which would otherwise be hashed and queried.
/// </para>
/// </remarks>
public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    /// <summary>
    /// The length the generator produces: 32 random bytes, base64url-encoded, which is 43 characters.
    /// </summary>
    public const int TokenLength = 43;

    /// <summary>
    /// The longest code this endpoint reads before giving up, with room for the spaces and dashes a
    /// person adds when copying six characters out of a mail client.
    /// </summary>
    public const int CodeLength = 32;

    /// <summary>The longest address, matching the column — the same bound registration uses.</summary>
    public const int EmailLength = 320;

    /// <summary>Declares the rules.</summary>
    public VerifyEmailCommandValidator()
    {
        // One credential or the other. Neither is `NotEmpty` on its own, because either one alone is a
        // complete request and insisting on both would refuse every link in existence.
        RuleFor(command => command)
            .Must(command => command.Token is { Length: > 0 } || command.Code is { Length: > 0 })
            .WithErrorCode(ValidationCodes.TokenRequired)
            // camelCase, spelled out: `OverridePropertyName` names a key and bypasses the global
            // resolver that would otherwise lower-case it — ADR-0043, which is the whole reason that
            // distinction is written down.
            .OverridePropertyName("token");

        RuleFor(command => command.Token)
            .MaximumLength(TokenLength).WithErrorCode(ValidationCodes.TokenTooLong)
            .When(command => command.Token is not null);

        RuleFor(command => command.Code)
            .MaximumLength(CodeLength).WithErrorCode(ValidationCodes.TokenTooLong)
            .When(command => command.Code is not null);

        RuleFor(command => command.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.EmailRequired)
            .MaximumLength(EmailLength).WithErrorCode(ValidationCodes.EmailTooLong)
            .When(command => command.Code is { Length: > 0 });
    }
}
