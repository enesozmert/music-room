using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// Spends a verification token exactly once — SDD §10.1 step 7.
/// </summary>
/// <param name="Token">The plaintext token the caller received by mail. It is hashed in the handler
/// and never logged, echoed back or stored.</param>
public sealed record VerifyEmailCommand(string Token) : IRequest<VerifyEmailOutcome>;
