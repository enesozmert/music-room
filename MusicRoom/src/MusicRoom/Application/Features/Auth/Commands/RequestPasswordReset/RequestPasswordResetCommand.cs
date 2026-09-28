using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Asks for a password reset link — SDD §13.1.
/// </summary>
/// <param name="Email">The address the account is known by.</param>
public sealed record RequestPasswordResetCommand(string Email) : IRequest, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.request-password-reset";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}
