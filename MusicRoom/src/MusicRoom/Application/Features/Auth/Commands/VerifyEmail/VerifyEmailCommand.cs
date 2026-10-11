using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Spends a verification credential exactly once — SDD §10.1 step 7, SPEC-052.
/// </summary>
/// <param name="Token">The plaintext token from the link, or null when a code was sent instead. Hashed in
/// the handler and never logged, echoed back or stored.</param>
/// <param name="Email">The address the code was sent to, when verifying by code. Null for the link, which
/// identifies its own row.</param>
/// <param name="Code">The six characters the caller typed, or null when the link was used.</param>
/// <remarks>
/// **One command, two credentials**, because they prove the same thing and spend the same row. Two
/// commands would be two handlers, two audit actions and two places for the rule about what a refusal may
/// say — and that rule is the delicate part of this flow.
/// </remarks>
public sealed record VerifyEmailCommand(string? Token, string? Email = null, string? Code = null)
    : IRequest<VerifyEmailOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.verify-email";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}
