using MusicRoom.Application.Abstractions.Auditing;
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
    private readonly IClientContextAccessor _clientContext;

    /// <summary>Takes the refresh-token write path, the access-token issuer and the client context.</summary>
    /// <param name="refreshTokens">The refresh-token write path.</param>
    /// <param name="accessTokens">The access-token issuer.</param>
    /// <param name="clientContext">The platform, device and app version the session is remembered by —
    /// SPEC-010 escalation 2. It is the same accessor the audit behaviour reads, so no new plumbing crosses a
    /// boundary.</param>
    public SessionIssuer(
        IRefreshTokenRepository refreshTokens,
        IAccessTokenIssuer accessTokens,
        IClientContextAccessor clientContext)
    {
        _refreshTokens = refreshTokens;
        _accessTokens = accessTokens;
        _clientContext = clientContext;
    }

    /// <summary>Issues the first pair of a new session.</summary>
    /// <param name="userId">The account signing in.</param>
    /// <param name="grantedScope">
    /// The scopes the authorization request asked for, or null for a first-party session — SPEC-045.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The pair the caller is answered with.</returns>
    /// <remarks>
    /// Null is what the credential endpoints pass: nobody asked for a scope there, because the client is the
    /// account's own. `GET /oauth2/userinfo` reads this through the session and answers accordingly, which is
    /// why the value is written here rather than into the access token — SDD §13.1 fixes that token's claims.
    /// </remarks>
    public async Task<AuthTokenPair> IssueAsync(
        Guid userId,
        string? grantedScope,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        string plaintext = RefreshTokenSecret.Create();

        // The middleware refuses a mutating request without these, so a sign-in always has them. Failing
        // loudly beats writing a session nobody can recognise in their own session list.
        ClientContext client = _clientContext.Current
            ?? throw new InvalidOperationException(
                "A session cannot be issued without client context: the mandatory client headers are "
                + "validated by middleware — SDD §10.7, SPEC-010.");

        // A login starts its own family. Everything rotated from it carries this id, so one indexed UPDATE
        // can revoke the whole lineage, and the access token names it as `sid`.
        Guid familyId = Guid.NewGuid();

        RefreshToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = OpaqueTokenHash.Of(plaintext),
            ExpiresAtUtc = nowUtc.AddDays(RefreshTokenLifetimeDays),
            Platform = client.Platform,
            Device = client.Device,
            AppVersion = client.AppVersion,
            Scope = grantedScope,
        };

        await _refreshTokens.IssueAsync(token, cancellationToken).ConfigureAwait(false);

        IssuedAccessToken accessToken = _accessTokens.Issue(userId, familyId, nowUtc);

        return new AuthTokenPair(accessToken.Token, plaintext, accessToken.ExpiresInSeconds);
    }
}

/// <summary>
/// What the credential endpoints pass as a granted scope — SPEC-045 decision 2.
/// </summary>
/// <remarks>
/// A named constant rather than a bare `null` at four call sites, because `null` there reads as an oversight
/// and this one is a decision: a session from `POST /auth/sessions` or `POST /auth/sessions/social` has no
/// granted scope, since nobody asked for one. `GET /oauth2/userinfo` answers such a caller the full claim
/// set — it is the person's own account, and `GET /me` already returns strictly more.
/// <para>
/// It shares this file with the issuer it is passed to, the way <c>RefreshTokenSecret</c> shares
/// <c>AuthTokenPair.cs</c>: it exists only as that method's argument.
/// </para>
/// </remarks>
public static class FirstPartySession
{
    /// <summary>No scope was granted, because none was asked for.</summary>
    public const string? NoGrantedScope = null;
}
