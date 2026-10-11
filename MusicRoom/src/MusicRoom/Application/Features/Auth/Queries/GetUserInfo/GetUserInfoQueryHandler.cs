using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Queries.GetUserInfo;

/// <summary>
/// Answers the claims the session's grant allows, and nothing beyond it — SPEC-045 decision 4.
/// </summary>
/// <remarks>
/// **The grant is read from the session, not from the request.** The access token names the session in its
/// <c>sid</c>, and the scopes granted when that session was opened are a column on its refresh-token family.
/// A scope sent as a parameter here would be a client granting itself whatever it asked for.
/// <para>
/// **A session with no granted scope answers everything.** That is a session from the credential endpoints,
/// where nobody asked for a scope because the client is the account's own — SPEC-045 decision 2. `GET /me`
/// already returns strictly more to the same caller, so filtering it here would protect nothing and would
/// break the only client this API has.
/// </para>
/// </remarks>
internal sealed class GetUserInfoQueryHandler : IRequestHandler<GetUserInfoQuery, UserInfoClaims?>
{
    /// <summary>The scope that asks for the display name — OpenID Connect Core 1.0 §5.4.</summary>
    internal const string ProfileScope = "profile";

    /// <summary>The scope that asks for the address and whether it is verified.</summary>
    internal const string EmailScope = "email";

    private static readonly char[] _separators = [' '];

    private readonly IIdentityClaimsReader _claims;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ICurrentUserAccessor _caller;

    /// <summary>Takes the claim reader, the session store and the caller.</summary>
    /// <param name="claims">Where the three claims are projected from.</param>
    /// <param name="refreshTokens">Where a session's granted scope lives.</param>
    /// <param name="caller">The account and session the validated token names.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetUserInfoQueryHandler(
        IIdentityClaimsReader claims,
        IRefreshTokenRepository refreshTokens,
        ICurrentUserAccessor caller)
    {
        _claims = claims;
        _refreshTokens = refreshTokens;
        _caller = caller;
    }

    /// <summary>Reads the grant, then the claims, then filters one by the other.</summary>
    /// <param name="request">The query, which carries nothing.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The claims, or null when the account behind the token no longer exists.</returns>
    public async Task<UserInfoClaims?> Handle(GetUserInfoQuery request, CancellationToken cancellationToken)
    {
        // Behind RequireAuthorization, so an absent id is a wiring fault rather than an anonymous caller.
        Guid userId = _caller.UserId
            ?? throw new InvalidOperationException(
                "UserInfo requires a bearer token — OpenID Connect Core 1.0 §5.3.");

        Guid sessionId = _caller.SessionId
            ?? throw new InvalidOperationException(
                "A validated access token always carries `sid` — SPEC-010.");

        GrantedScope grant = await _refreshTokens
            .FindGrantedScopeAsync(userId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        IdentityClaims? claims = await _claims.FindAsync(userId, cancellationToken).ConfigureAwait(false);

        if (claims is null)
        {
            // The token is valid and its account is gone — deleted while this token was still alive. There
            // is nothing to assert about it, and inventing a `sub` for a row that no longer exists would be
            // asserting that it does.
            return null;
        }

        return Filter(userId, claims, grant);
    }

    /// <summary>
    /// Applies the scope rule: <c>openid</c> gives <c>sub</c>, and each further scope one more claim.
    /// </summary>
    /// <remarks>
    /// A session whose family could not be found answers the minimal set rather than the full one. It cannot
    /// happen while a token is valid — family rows are stamped, never deleted — and the safe direction for an
    /// impossible case is the narrow one.
    /// </remarks>
    private static UserInfoClaims Filter(Guid userId, IdentityClaims claims, GrantedScope grant)
    {
        string subject = userId.ToString();

        if (!grant.Found)
        {
            return new UserInfoClaims(subject, null, null, null);
        }

        if (grant.Scope is null)
        {
            // A first-party session: everything this provider can assert — decision 2.
            return new UserInfoClaims(subject, claims.Email, claims.IsEmailVerified, claims.DisplayName);
        }

        string[] granted = grant.Scope.Split(_separators, StringSplitOptions.RemoveEmptyEntries);

        bool email = granted.Contains(EmailScope, StringComparer.Ordinal);
        bool profile = granted.Contains(ProfileScope, StringComparer.Ordinal);

        return new UserInfoClaims(
            subject,
            email ? claims.Email : null,
            email ? claims.IsEmailVerified : null,
            profile ? claims.DisplayName : null);
    }
}
