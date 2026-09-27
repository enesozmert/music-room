using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.Login;

/// <summary>
/// Verifies a password and opens a session — SDD §13.1.
/// </summary>
/// <remarks>
/// Every failure — unknown address, wrong password, unverified account — returns null, and the
/// endpoint turns that into one bodiless <c>401</c>. Distinguishing them would tell an anonymous
/// caller which addresses hold an account.
/// </remarks>
internal sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenPair?>
{
    /// <summary>
    /// The absolute lifetime of a refresh-token family — owner decision, 2026-09-27. Absolute, not
    /// sliding: every replacement inherits this expiry from the family's first token, so a stolen
    /// family cannot be kept alive by rotating it.
    /// </summary>
    internal const int RefreshTokenLifetimeDays = 30;

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly ICredentialAttemptLimiter _attempts;

    /// <summary>Takes the two write paths, the hasher, the token issuer and the attempt limiter.</summary>
    /// <param name="users">The account read path.</param>
    /// <param name="refreshTokens">The refresh-token write path.</param>
    /// <param name="passwordHasher">The password verifier — ADR-0007.</param>
    /// <param name="accessTokens">The access-token issuer.</param>
    /// <param name="attempts">The per-account half of the credential rate limit — SPEC-006.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public LoginCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IAccessTokenIssuer accessTokens,
        ICredentialAttemptLimiter attempts)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _accessTokens = accessTokens;
        _attempts = attempts;
    }

    /// <summary>Issues a token pair, or null for every failure.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The pair on success, null otherwise.</returns>
    /// <remarks>Neither the password nor either issued token is ever logged here.</remarks>
    public async Task<AuthTokenPair?> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Lower-cased on the way in, the way registration stores it — the column and ux_users_email
        // both hold the normalised form. It is also the rate-limit partition key.
        string email = request.Email.Trim().ToLowerInvariant();

        // Asked before any hashing and before the read: the point of the limit is that a refused
        // attempt is cheap. A refusal costs the same whether or not the address exists, so this check
        // is not an enumeration oracle either.
        CredentialAttemptDecision decision = _attempts.Check(email);

        if (!decision.Allowed)
        {
            throw new CredentialAttemptThrottledException(decision.RetryAfter);
        }

        UserCredential? credential = await _users
            .FindCredentialByEmailAsync(email, cancellationToken)
            .ConfigureAwait(false);

        bool passwordMatches = VerifyInConstantWork(request.Password, credential);

        if (!passwordMatches || credential is not { IsEmailVerified: true })
        {
            // Only a failure is charged. A correct password is not abuse, and an unverified account
            // that keeps trying is: the mail it needs is a different endpoint, with its own limit.
            _attempts.RecordChargeableAttempt(email);

            return null;
        }

        // A successful sign-in clears the history, so a user who mistyped twice and then got it right
        // does not carry a penalty into tomorrow.
        _attempts.Reset(email);

        return await IssueSessionAsync(credential.UserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spends the same BCrypt work whether or not an account was found.
    /// </summary>
    /// <remarks>
    /// With no row, the candidate is hashed and the result thrown away: hashing and verifying run the
    /// same cost factor, so the two paths take the same time. An endpoint that answers faster for an
    /// unknown address is an enumeration oracle with a stopwatch. The unverified case needs no
    /// special handling — it has a stored hash and is verified like any other.
    /// </remarks>
    private bool VerifyInConstantWork(string password, UserCredential? credential)
    {
        if (credential is null)
        {
            _ = _passwordHasher.Hash(password);
            return false;
        }

        return _passwordHasher.Verify(password, credential.PasswordHash);
    }

    /// <summary>
    /// Opens a new refresh-token family and mints the first pair of the session.
    /// </summary>
    private async Task<AuthTokenPair> IssueSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        string plaintext = RefreshTokenSecret.Create();

        RefreshToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // A login starts its own family. Everything rotated from it carries this id, so one
            // indexed UPDATE can revoke the whole lineage if a spent token is ever replayed.
            FamilyId = Guid.NewGuid(),
            TokenHash = OpaqueTokenHash.Of(plaintext),
            ExpiresAtUtc = nowUtc.AddDays(RefreshTokenLifetimeDays),
        };

        await _refreshTokens.IssueAsync(token, cancellationToken).ConfigureAwait(false);

        IssuedAccessToken accessToken = _accessTokens.Issue(userId, nowUtc);

        return new AuthTokenPair(accessToken.Token, plaintext, accessToken.ExpiresInSeconds);
    }
}
