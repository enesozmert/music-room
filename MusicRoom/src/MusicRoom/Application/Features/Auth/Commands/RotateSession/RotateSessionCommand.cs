using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.RotateSession;

/// <summary>
/// Spends a refresh token exactly once and returns a rotated pair — SDD §13.1.
/// </summary>
/// <param name="RefreshToken">The plaintext refresh token the client holds. It is hashed in the
/// handler and never logged, echoed back or stored.</param>
public sealed record RotateSessionCommand(string RefreshToken) : IRequest<AuthTokenPair?>;
