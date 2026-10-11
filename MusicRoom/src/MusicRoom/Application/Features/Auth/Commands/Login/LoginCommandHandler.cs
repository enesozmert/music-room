using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.Login;

/// <summary>
/// Verifies a password and opens a session — SDD §13.1.
/// </summary>
/// <remarks>
/// Every failure — unknown address, wrong password, unverified account — answers
/// <see cref="LoginResult.Refused"/>, which the endpoint turns into one bodiless <c>401</c>.
/// Distinguishing them would tell an anonymous caller which addresses hold an account. Being out of
/// attempts is the one refusal the endpoint answers differently, because the contract lists `429`.
/// </remarks>
internal sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SessionIssuer _sessions;
    private readonly ICredentialAttemptLimiter _attempts;
    private readonly IAuditContext _audit;

    /// <summary>Takes the account read path, the hasher, the session issuer, the limiter and the audit context.</summary>
    /// <param name="users">The account read path.</param>
    /// <param name="passwordHasher">The password verifier — ADR-0007.</param>
    /// <param name="sessions">Opens the refresh-token family and mints the pair.</param>
    /// <param name="attempts">The per-account half of the credential rate limit — SPEC-006.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        SessionIssuer sessions,
        ICredentialAttemptLimiter attempts,
        IAuditContext audit)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _sessions = sessions;
        _attempts = attempts;
        _audit = audit;
    }

    /// <summary>Issues a token pair, or reports the refusal.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The pair on success, a refusal otherwise — out of attempts or not.</returns>
    /// <remarks>
    /// Neither the password nor either issued token is ever logged here. What became of the attempt goes to
    /// the audit context, so the row says <c>refused</c>, <c>throttled</c> or <c>completed</c> — SPEC-009.
    /// </remarks>
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
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
            // The loudest row the table holds: somebody is working through passwords for this address.
            _audit.SetOutcome(AuditOutcome.Throttled);

            return LoginResult.OutOfAttempts(new CredentialThrottle(decision.RetryAfter));
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

            // Refused, and deliberately without naming an account: a user_id on a failed login would make
            // the table an account-existence oracle for anyone who can read it.
            _audit.SetOutcome(AuditOutcome.Refused);

            return LoginResult.Refused;
        }

        // A successful sign-in clears the history, so a user who mistyped twice and then got it right
        // does not carry a penalty into tomorrow.
        _attempts.Reset(email);

        _audit.SetUserId(credential.UserId);
        _audit.SetTargetId(credential.UserId);

        return LoginResult.Issued(
            await _sessions
                .IssueAsync(credential.UserId, FirstPartySession.NoGrantedScope, cancellationToken)
                .ConfigureAwait(false));
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
        // A social-only account has no hash — SPEC-041. It costs the same work as an unknown address and
        // answers the same generic refusal: saying "this address exists but has no password" would be the
        // enumeration oracle this method exists to avoid.
        if (credential?.PasswordHash is null)
        {
            _ = _passwordHasher.Hash(password);
            return false;
        }

        return _passwordHasher.Verify(password, credential.PasswordHash);
    }
}
