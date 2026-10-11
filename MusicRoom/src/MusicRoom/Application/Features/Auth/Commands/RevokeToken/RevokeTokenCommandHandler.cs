using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.RevokeToken;

/// <summary>
/// Ends the session a presented refresh token belongs to — RFC 7009, SPEC-045 decision 5.
/// </summary>
/// <remarks>
/// **The whole family, not the one row.** A session is a refresh-token family (ADR-0032); revoking a single
/// row would leave its replacement live, which is a revocation that does not revoke.
/// <para>
/// Server-side, like logout: after this, a kept copy of any token in the family is worthless. The access
/// token already issued stays valid until it expires — at most 15 minutes and 30 seconds — which is the
/// window the threat register accepts rather than an oversight, and the reason
/// <c>token_type_hint=access_token</c> is refused instead of answered with a hopeful `200`.
/// </para>
/// </remarks>
internal sealed class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, RevokeTokenOutcome>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditContext _audit;

    /// <summary>Takes the refresh-token write path and the audit context.</summary>
    /// <param name="refreshTokens">Where the family is revoked.</param>
    /// <param name="audit">Where this handler states what became of the request — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RevokeTokenCommandHandler(IRefreshTokenRepository refreshTokens, IAuditContext audit)
    {
        _refreshTokens = refreshTokens;
        _audit = audit;
    }

    /// <summary>Hashes, revokes, and records which of the two things happened.</summary>
    /// <param name="request">The presented token.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>Whether anything was revoked. The endpoint answers `200` either way.</returns>
    /// <remarks>
    /// Neither the token nor its digest is logged. A digest of a live credential is still the lookup key
    /// that spends it.
    /// </remarks>
    public async Task<RevokeTokenOutcome> Handle(
        RevokeTokenCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool revoked = await _refreshTokens
            .TryRevokeByTokenAsync(
                OpaqueTokenHash.Of(request.Token),
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (!revoked)
        {
            // Nothing happened, and the caller is told the same thing as if something had. The audit row is
            // where the two stop being identical, because a stream of revocations that revoke nothing is
            // somebody trying tokens.
            _audit.SetOutcome(AuditOutcome.Refused);

            return RevokeTokenOutcome.NothingToRevoke;
        }

        return RevokeTokenOutcome.Revoked;
    }
}
