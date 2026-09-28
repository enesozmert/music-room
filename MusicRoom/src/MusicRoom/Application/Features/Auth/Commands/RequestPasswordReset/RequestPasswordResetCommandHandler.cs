using MediatR;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;

/// <summary>
/// Issues a reset token for an account that exists and mails the link — SDD §13.1.
/// </summary>
internal sealed class RequestPasswordResetCommandHandler : IRequestHandler<RequestPasswordResetCommand>
{
    private readonly IUserRepository _users;
    private readonly IMailService _mail;
    private readonly ICredentialAttemptLimiter _attempts;
    private readonly ILogger<RequestPasswordResetCommandHandler> _logger;

    /// <summary>Takes the write path, the mail sender and the attempt limiter.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="mail">The mail sender.</param>
    /// <param name="attempts">The per-account half of the credential rate limit — SPEC-006.</param>
    /// <param name="logger">The handler's logger.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RequestPasswordResetCommandHandler(
        IUserRepository users,
        IMailService mail,
        ICredentialAttemptLimiter attempts,
        ILogger<RequestPasswordResetCommandHandler> logger)
    {
        _users = users;
        _mail = mail;
        _attempts = attempts;
        _logger = logger;
    }

    /// <summary>Sends a reset link, or does nothing visible when there is no account to reset.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task Handle(RequestPasswordResetCommand request, CancellationToken cancellationToken)
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
            throw new CredentialAttemptThrottledException(decision.RetryAfter);
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
            // account — the same rule registration and resend follow.
            return;
        }

        // After the insert committed, never inside it — ADR-0013.
        await SendResetLinkAsync(email, issued.Plaintext, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendResetLinkAsync(
        string emailAddress,
        string plaintextToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mail
                .SendPasswordResetAsync(emailAddress, plaintextToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed for the reason registration swallows it: letting this escape would turn an
            // existing account into a 500 while an unknown address still answered 202, which is the
            // timing-free enumeration oracle this endpoint exists inside. The token is written, so the
            // caller can ask again.
            _logger.LogWarning(
                exception,
                "Password reset mail could not be sent. The token was issued.");
        }
    }
}
