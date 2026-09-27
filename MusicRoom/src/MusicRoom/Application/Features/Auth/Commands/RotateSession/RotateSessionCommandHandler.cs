using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.RotateSession;

/// <summary>
/// Spends a refresh token once and mints its replacement — SDD §13.1.
/// </summary>
/// <remarks>
/// Rotation is arbitrated in the database, not here: whoever affects the row won the exchange.
/// Presenting an already-spent token revokes the whole family, which is the theft detection the
/// design requires — and it, an unknown token and an expired one all leave as one bodiless
/// <c>401</c>.
/// </remarks>
internal sealed class RotateSessionCommandHandler : IRequestHandler<RotateSessionCommand, AuthTokenPair?>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly IAuditContext _audit;

    /// <summary>Takes the refresh-token write path, the access-token issuer and the audit context.</summary>
    /// <param name="refreshTokens">The refresh-token write path.</param>
    /// <param name="accessTokens">The access-token issuer.</param>
    /// <param name="audit">Where this handler states what became of the exchange — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RotateSessionCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IAccessTokenIssuer accessTokens,
        IAuditContext audit)
    {
        _refreshTokens = refreshTokens;
        _accessTokens = accessTokens;
        _audit = audit;
    }

    /// <summary>Rotates the pair, or returns null for every failure.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The rotated pair on success, null otherwise.</returns>
    /// <remarks>
    /// Neither the presented token nor its digest is ever logged here. A digest of a live credential
    /// is still the lookup key that spends it.
    /// </remarks>
    public async Task<AuthTokenPair?> Handle(
        RotateSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;
        string plaintext = RefreshTokenSecret.Create();

        // UserId, FamilyId and ExpiresAtUtc are filled in by the repository, inside the transaction
        // that spends the presented row: the family and its absolute expiry are read there, never
        // from an earlier read here.
        RefreshToken replacement = new()
        {
            Id = Guid.NewGuid(),
            TokenHash = OpaqueTokenHash.Of(plaintext),
        };

        RefreshTokenRotation rotation = await _refreshTokens
            .TryRotateAsync(
                OpaqueTokenHash.Of(request.RefreshToken),
                replacement,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (rotation.Status != RefreshTokenRotationStatus.Rotated)
        {
            // Reused revoked the family inside the repository; rejected had nothing to revoke. Both answer
            // the caller identically — and the audit row is where they stop being identical, because a
            // reuse means a refresh token leaked and an operator needs to see that.
            if (rotation.Status == RefreshTokenRotationStatus.Reused)
            {
                _audit.SetOutcome(AuditOutcome.ReuseDetected);
                _audit.SetUserId(rotation.UserId);
                _audit.SetTargetId(rotation.UserId);
            }
            else
            {
                _audit.SetOutcome(AuditOutcome.Refused);
            }

            return null;
        }

        _audit.SetUserId(rotation.UserId);
        _audit.SetTargetId(rotation.UserId);

        IssuedAccessToken accessToken = _accessTokens.Issue(rotation.UserId, nowUtc);

        return new AuthTokenPair(accessToken.Token, plaintext, accessToken.ExpiresInSeconds);
    }
}
