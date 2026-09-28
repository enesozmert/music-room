using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.RotateSession;

/// <summary>
/// Spends a refresh token exactly once and returns a rotated pair — SDD §13.1.
/// </summary>
/// <param name="RefreshToken">The plaintext refresh token the client holds. It is hashed in the
/// handler and never logged, echoed back or stored.</param>
public sealed record RotateSessionCommand(string RefreshToken) : IRequest<AuthTokenPair?>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.rotate-session";

    /// <inheritdoc />
    public string AuditTargetType => "session";
}
