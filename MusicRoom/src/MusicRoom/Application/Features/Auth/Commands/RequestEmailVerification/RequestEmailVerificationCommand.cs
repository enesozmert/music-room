using MediatR;

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
public sealed record RequestEmailVerificationCommand(string Email) : IRequest;
