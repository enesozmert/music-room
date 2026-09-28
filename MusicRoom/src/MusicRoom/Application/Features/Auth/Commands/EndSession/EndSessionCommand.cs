using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.EndSession;

/// <summary>
/// Ends a session — `DELETE /api/v1/auth/sessions/current` and `DELETE /api/v1/auth/sessions/{sessionId}`.
/// </summary>
/// <param name="SessionId">The family to revoke, or null to mean "the one this token belongs to", which the
/// handler resolves from the <c>sid</c> claim — SPEC-010 escalation 1.</param>
/// <remarks>
/// Logout is server-side. A client discarding its tokens is not logout: `authentication-authorization` says
/// so, and the revoked family is what makes it true for anyone who kept a copy.
/// </remarks>
public sealed record EndSessionCommand(Guid? SessionId) : IRequest<EndSessionOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.logout";

    /// <inheritdoc />
    public string AuditTargetType => "session";
}
