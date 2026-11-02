using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Spends a reset credential exactly once and replaces the password — SDD §13.1, SPEC-055.
/// </summary>
/// <param name="NewPassword">The replacement password. Hashed with BCrypt and never logged.</param>
/// <param name="Token">The plaintext token from the link, or null when a code was typed instead. Hashed
/// in the handler and never logged, echoed back or stored.</param>
/// <param name="Email">The address the code was sent to, when resetting by code. Null for the link,
/// which identifies its own row.</param>
/// <param name="Code">The six characters the caller typed, or null when the link was used.</param>
/// <remarks>
/// **One command, two credentials**, as email verification already does: they prove the same thing and
/// spend the same row, and two commands would be two handlers, two audit actions and two places for the
/// rule about what a refusal may say.
/// <para>
/// The password comes first in the parameter list because it is the one value every call carries.
/// </para>
/// </remarks>
public sealed record ResetPasswordCommand(
    string NewPassword,
    string? Token = null,
    string? Email = null,
    string? Code = null) : IRequest<ResetPasswordOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.reset-password";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}
