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

    /// <summary>Takes the two write paths, the hasher and the token issuer.</summary>
    /// <param name="users">The account read path.</param>
    /// <param name="refreshTokens">The refresh-token write path.</param>
    /// <param name="passwordHasher">The password verifier — ADR-0007.</param>
    /// <param name="accessTokens">The access-token issuer.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public LoginCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IAccessTokenIssuer accessTokens)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _accessTokens = accessTokens;
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
        // both hold the normalised form.
        string email = request.Email.Trim().ToLowerInvariant();

        UserCredential? credential = await _users
            .FindCredentialByEmailAsync(email, cancellationToken)
            .ConfigureAwait(false);

        bool passwordMatches = VerifyInConstantWork(request.Password, credential);

        return passwordMatches && credential is { IsEmailVerified: true }
            ? await IssueSessionAsync(credential.UserId, cancellationToken).ConfigureAwait(false)
            : null;
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
