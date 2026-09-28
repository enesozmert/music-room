using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.RequestEmailVerification;

/// <summary>
/// Asks for a second verification link for an address — SDD §10.1.
/// </summary>
/// <param name="Email">The address to mail. It is never echoed back.</param>
/// <remarks>
/// The command returns nothing. The endpoint answers <c>202</c> whether the address is unknown,
/// unverified or already verified, so a result carrying which of those it was would be an
/// account-enumeration oracle waiting to be serialised — SPEC-004, owner decision 2026-09-27.
/// </remarks>
public sealed record RequestEmailVerificationCommand(string Email) : IRequest, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.request-verification";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}
