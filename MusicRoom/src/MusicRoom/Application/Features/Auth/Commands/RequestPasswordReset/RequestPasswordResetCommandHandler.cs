using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Issues a reset token for an account that exists and mails the link — SDD §13.1.
/// </summary>
internal sealed class RequestPasswordResetCommandHandler : IRequestHandler<RequestPasswordResetCommand, CredentialThrottle?>
{
    private readonly IUserRepository _users;
    private readonly CredentialMailDispatcher _mail;
    private readonly ICredentialAttemptLimiter _attempts;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the mail sender and the attempt limiter.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="mail">The mail sender, which swallows and logs a send failure.</param>
    /// <param name="attempts">The per-account half of the credential rate limit — SPEC-006.</param>
    /// <param name="audit">Where this handler states what became of the attempt — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RequestPasswordResetCommandHandler(
        IUserRepository users,
        CredentialMailDispatcher mail,
        ICredentialAttemptLimiter attempts,
        IAuditContext audit)
    {
        _users = users;
        _mail = mail;
        _attempts = attempts;
        _audit = audit;
    }

    /// <summary>Sends a reset link, or does nothing visible when there is no account to reset.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<CredentialThrottle?> Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;

        // Lower-cased exactly as registration stores it, so Alice@example.com finds the account
        // alice@example.com registered. It is also the rate-limit partition key.
        string email = request.Email.Trim().ToLowerInvariant();

        // Every attempt is charged, not only one that finds an account: each success writes a row and,
        // once O17 lands a real sender, spends mail quota — and a limit that only counted successes
        // would tell a caller which addresses exist by how long it took to be refused.
        CredentialAttemptDecision decision = _attempts.Check(email);

        if (!decision.Allowed)
        {
            // Recorded rather than thrown, so the row survives — a reset-link flood at one address.
            _audit.SetOutcome(AuditOutcome.Throttled);

            return new CredentialThrottle(decision.RetryAfter);
        }

        _attempts.RecordChargeableAttempt(email);

        // The account id is left empty on purpose: resolving an address to an account is a read this
        // layer must not perform, because doing it here and then inserting would be a read-then-write
        // check outside the transaction that arbitrates it. The write path stamps the id it resolved.
        IssuedPasswordResetToken issued = PasswordResetTokenIssuer.Issue(Guid.Empty, nowUtc);

        bool written = await _users
            .TryIssuePasswordResetTokenAsync(email, issued.Token, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!written)
        {
            // An unknown address sends nothing and is answered 202 all the same. A mail, or its absence
            // being observable any other way, would tell an anonymous caller which addresses hold an
            // account — the same rule registration and resend follow. The audit row stays `completed`.
            return null;
        }

        // After the insert committed, never inside it — ADR-0013.
        // No display name, for the reason the resend path gives: this is keyed on an address and reads
        // no account.
        await _mail
            .SendPasswordResetAsync(email, string.Empty, issued.Plaintext, cancellationToken)
            .ConfigureAwait(false);

        return null;
    }
}
