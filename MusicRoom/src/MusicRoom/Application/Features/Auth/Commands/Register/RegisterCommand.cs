using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Registers an account with an address and a password — SDD §10.1.
/// </summary>
/// <param name="Email">The address to register.</param>
/// <param name="Password">The plaintext password. It is hashed in the handler and never stored,
/// logged or echoed back.</param>
/// <param name="DisplayName">The name other users see.</param>
/// <remarks>
/// The command returns nothing. The endpoint answers <c>202</c> whether or not the address was
/// free, so a result carrying that fact would be an account-enumeration oracle waiting to be
/// serialised — SDD §10.1 step 5.
/// </remarks>
public sealed record RegisterCommand(string Email, string Password, string DisplayName) : IRequest, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "auth.register";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}
