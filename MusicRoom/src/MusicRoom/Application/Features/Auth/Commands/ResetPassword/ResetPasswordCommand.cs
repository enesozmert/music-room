using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Spends a reset token once and replaces the password — SDD §13.1.
/// </summary>
/// <param name="Token">The plaintext token the caller received by mail. It is hashed in the handler and
/// never logged, echoed back or stored.</param>
/// <param name="NewPassword">The replacement password. Hashed with BCrypt and never logged.</param>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<ResetPasswordOutcome>;
