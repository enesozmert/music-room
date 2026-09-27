using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// Opens a new refresh-token family and mints the first pair of a session — SDD §13.1.
/// </summary>
/// <remarks>
/// Extracted from <c>LoginCommandHandler</c> when SPEC-009 added the audit context and pushed that
/// constructor to six parameters, which `SonarLint.xml` refuses (S107). It is also where the social sign-in
/// of slice S8 will issue its pair, so the extraction is not only a limit being respected.
/// </remarks>
public sealed class SessionIssuer
{
    /// <summary>
    /// The absolute lifetime of a refresh-token family — owner decision, 2026-09-27, recorded in
    /// `docs/SDD.md` §13.1. Absolute, not sliding: every replacement inherits this expiry, so a stolen
    /// family cannot be kept alive by rotating it.
    /// </summary>
    public const int RefreshTokenLifetimeDays = 30;

    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenIssuer _accessTokens;

    /// <summary>Takes the refresh-token write path and the access-token issuer.</summary>
    /// <param name="refreshTokens">The refresh-token write path.</param>
    /// <param name="accessTokens">The access-token issuer.</param>
    public SessionIssuer(IRefreshTokenRepository refreshTokens, IAccessTokenIssuer accessTokens)
    {
        _refreshTokens = refreshTokens;
        _accessTokens = accessTokens;
    }

    /// <summary>Issues the first pair of a new session.</summary>
    /// <param name="userId">The account signing in.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The pair the caller is answered with.</returns>
    public async Task<AuthTokenPair> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        string plaintext = RefreshTokenSecret.Create();

        RefreshToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // A login starts its own family. Everything rotated from it carries this id, so one indexed
            // UPDATE can revoke the whole lineage if a spent token is ever replayed.
            FamilyId = Guid.NewGuid(),
            TokenHash = OpaqueTokenHash.Of(plaintext),
            ExpiresAtUtc = nowUtc.AddDays(RefreshTokenLifetimeDays),
        };

        await _refreshTokens.IssueAsync(token, cancellationToken).ConfigureAwait(false);

        IssuedAccessToken accessToken = _accessTokens.Issue(userId, nowUtc);

        return new AuthTokenPair(accessToken.Token, plaintext, accessToken.ExpiresInSeconds);
    }
}
