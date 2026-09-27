using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Asks for a password reset link — SDD §13.1.
/// </summary>
/// <param name="Email">The address the account is known by.</param>
public sealed record RequestPasswordResetCommand(string Email) : IRequest;
