using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.ChangePassword;

/// <summary>
/// Verifies the current password, replaces it, and ends every other session — SPEC-028, SDD §13.1.
/// </summary>
/// <remarks>
/// The route is authenticated, so the account comes from the validated token and never from the body: a change
/// request that named its own account would be an invitation to change somebody else's.
/// <para>
/// Knowing the current password is what ASVS V7.5.1 calls re-authentication before a sensitive attribute
/// changes. A stolen access token alone is therefore not enough to take an account over permanently — which is
/// also why this route is rate-limited: without the limiter it would be an oracle for guessing the password at
/// request speed.
/// </para>
/// <para>
/// Neither password is logged, and nothing returns a hash to a caller.
/// </para>
/// </remarks>
internal sealed class ChangePasswordCommandHandler
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICredentialAttemptLimiter _attempts;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the hasher, the limiter, the caller and the audit context.</summary>
    /// <param name="users">The account read and write path.</param>
    /// <param name="passwordHasher">The hasher — ADR-0007.</param>
    /// <param name="attempts">The per-account limiter — SDD §13.3.</param>
    /// <param name="currentUser">Who is calling, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ChangePasswordCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ICredentialAttemptLimiter attempts,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _attempts = attempts;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Changes the password, or says why it did not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into a status code.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller, or a token with no `sid`.</exception>
    public async Task<ChangePasswordResult> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId ?? throw NoCaller();
        Guid familyId = _currentUser.SessionId ?? throw NoCaller();

        _audit.SetUserId(userId);
        _audit.SetTargetId(userId);

        // Keyed on the account, which this request knows from its token rather than from a submitted address.
        string key = "account:" + userId.ToString("D");

        CredentialAttemptDecision decision = _attempts.Check(key);

        if (!decision.Allowed)
        {
            _audit.SetOutcome(AuditOutcome.Throttled);

            return ChangePasswordResult.Throttled(decision.RetryAfter);
        }

        UserCredential? credential = await _users
            .FindCredentialByIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        // A social-only account has nothing to replace, so it takes the same refusal a wrong current
        // password takes — SPEC-041. Setting a first password is the reset flow, which proves the address.
        if (credential?.PasswordHash is null
            || !_passwordHasher.Verify(request.CurrentPassword, credential.PasswordHash))
        {
            // Charged, because a wrong current password is exactly the attempt this limiter exists to bound.
            _attempts.RecordChargeableAttempt(key);
            _audit.SetOutcome(AuditOutcome.Refused);

            return ChangePasswordResult.Refused();
        }

        // After the current password is proven and before anything is written: this is an authenticated caller
        // choosing a new password, and the words come from the row already read rather than from a sixth
        // constructor dependency S107 would refuse — SPEC-035, ADR-0042 as corrected.
        if (ChosenFromContext(request.NewPassword, credential))
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return ChangePasswordResult.RefusedByContext();
        }

        // Hashed before the write path is asked anything, so the transaction holds no locks while BCrypt runs
        // for a quarter of a second.
        string newPasswordHash = _passwordHasher.Hash(request.NewPassword);

        bool changed = await _users
            .TryChangePasswordAsync(userId, newPasswordHash, familyId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!changed)
        {
            // The account disappeared between the read and the write — deleted by another request. Nothing was
            // written, and the caller is told the same thing a wrong password is told.
            _audit.SetOutcome(AuditOutcome.Refused);

            return ChangePasswordResult.Refused();
        }

        // A correct password is not abuse, so the history clears — login's rule.
        _attempts.Reset(key);
        _audit.SetOutcome(AuditOutcome.Completed);

        return ChangePasswordResult.Changed();
    }

    /// <summary>
    /// Whether the new password carries one of this account's own words — SPEC-035.
    /// </summary>
    /// <remarks>
    /// A method of its own because <c>Handle</c> is at S138's 40-line limit with the screen inlined. The limit
    /// is the reason, and a named predicate reads better than the call it replaced.
    /// </remarks>
    private static bool ChosenFromContext(string newPassword, UserCredential credential) =>
        PasswordContext.ContainsAContextWord(newPassword, credential.Email, credential.DisplayName);

    private static InvalidOperationException NoCaller() =>
        new("ChangePasswordCommand reached its handler without an authenticated caller and a session id. "
            + "The route requires authorisation — SPEC-028.");
}
