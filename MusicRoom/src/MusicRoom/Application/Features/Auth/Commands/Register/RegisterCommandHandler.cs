using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Hashes the password, writes the account, issues its verification token and mails the link —
/// SDD §10.1 steps 3 and 4.
/// </summary>
internal sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, CredentialThrottle?>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly CredentialMailDispatcher _mail;
    private readonly ICredentialAttemptLimiter _attempts;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the hasher, the mail sender and the attempt limiter; all
    /// abstractions this layer declares.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="passwordHasher">The password hasher — ADR-0007.</param>
    /// <param name="mail">The mail sender, which swallows and logs a send failure.</param>
    /// <param name="attempts">The per-account half of the credential rate limit — SPEC-006.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    public RegisterCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        CredentialMailDispatcher mail,
        ICredentialAttemptLimiter attempts,
        IAuditContext audit)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _mail = mail;
        _attempts = attempts;
        _audit = audit;
    }

    /// <summary>Registers the account, or does nothing visible when the address is already taken.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<CredentialThrottle?> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;
        string partitionKey = request.Email.Trim().ToLowerInvariant();

        // Charged before the BCrypt hash, which is the expensive part of this handler, and charged on
        // every attempt: a duplicate address answers 202 and writes nothing, but it still made the
        // service hash a password.
        CredentialAttemptDecision decision = _attempts.Check(partitionKey);

        if (!decision.Allowed)
        {
            // Recorded rather than thrown, so the row survives — the registration loop T-25 bounds.
            _audit.SetOutcome(AuditOutcome.Throttled);

            return new CredentialThrottle(decision.RetryAfter);
        }

        _attempts.RecordChargeableAttempt(partitionKey);

        User user = new()
        {
            Id = Guid.NewGuid(),
            // Lower-cased on the way in, so ux_users_email is case-insensitive without a functional
            // index: Alice@example.com and alice@example.com are one account, not two.
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            IsEmailVerified = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        IssuedVerificationToken issued = VerificationTokenIssuer.Issue(user.Id, nowUtc);

        bool registered = await _users
            .TryRegisterAsync(user, issued.Token, cancellationToken)
            .ConfigureAwait(false);

        if (!registered)
        {
            // A duplicate address sends nothing. A mail to an existing account saying "you just
            // registered" is an account-enumeration oracle with a delivery receipt — SDD §10.1
            // step 5. The caller is still answered 202, exactly as a fresh address would be.
            //
            // The audit row stays `completed` on purpose: a `refused` row here would tell anyone who can
            // read the table which addresses already hold an account.
            return null;
        }

        // After the insert committed, never inside it — ADR-0013.
        await _mail.SendVerificationAsync(user.Email, issued.Plaintext, cancellationToken).ConfigureAwait(false);

        _audit.SetUserId(user.Id);
        _audit.SetTargetId(user.Id);

        return null;
    }
}
