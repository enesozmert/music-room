using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.RevokeToken;

/// <summary>
/// Revokes a presented refresh token and its session — <c>POST /oauth2/revoke</c>, RFC 7009, SPEC-045.
/// </summary>
/// <param name="Token">The token, in plaintext as the client holds it. Hashed by the handler; the digest is
/// what the database is keyed by, and neither value is ever logged.</param>
/// <remarks>
/// No account: this route is anonymous and the token itself is the credential. Whoever holds a live refresh
/// token may end the session it belongs to, which is exactly what a client does at sign-out.
/// </remarks>
public sealed record RevokeTokenCommand(string Token) : IRequest<RevokeTokenOutcome>;

/// <summary>
/// What a revocation did — and what the caller is told, which is deliberately less.
/// </summary>
/// <remarks>
/// The endpoint answers `200` to both values. RFC 7009 §2.2 requires it: an unknown token must not be
/// distinguishable from a revoked one, or the endpoint becomes an oracle for which tokens exist. The
/// distinction lives in the audit record, where only an operator reads it.
/// </remarks>
public enum RevokeTokenOutcome
{
    /// <summary>A live family was revoked.</summary>
    Revoked = 0,

    /// <summary>The token is unknown, or its family was already revoked. Answered identically.</summary>
    NothingToRevoke = 1,
}
