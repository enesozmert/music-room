using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.Login;

/// <summary>
/// Exchanges an address and a password for a session — SDD §13.1.
/// </summary>
/// <param name="Email">The address the account is known by.</param>
/// <param name="Password">The plaintext candidate. It is verified in the handler and never logged,
/// echoed back or stored.</param>
public sealed record LoginCommand(string Email, string Password) : IRequest<AuthTokenPair?>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.login";

    /// <inheritdoc />
    public string AuditTargetType => "session";
}
