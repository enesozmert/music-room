using MediatR;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Auth.Commands.RequestEmailVerification;

/// <summary>
/// Issues a fresh verification token for an unverified account and mails the link — SPEC-004.
/// </summary>
internal sealed class RequestEmailVerificationCommandHandler : IRequestHandler<RequestEmailVerificationCommand>
{
    private readonly IUserRepository _users;
    private readonly IMailService _mail;
    private readonly ILogger<RequestEmailVerificationCommandHandler> _logger;

    /// <summary>Takes the write path and the mail sender; both abstractions this layer declares.</summary>
    /// <param name="users">The account write path.</param>
    /// <param name="mail">The mail sender.</param>
    /// <param name="logger">The handler's logger.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RequestEmailVerificationCommandHandler(
        IUserRepository users,
        IMailService mail,
        ILogger<RequestEmailVerificationCommandHandler> logger)
    {
        _users = users;
        _mail = mail;
        _logger = logger;
    }

    /// <summary>Sends a second link, or does nothing visible when there is nothing to verify.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task Handle(RequestEmailVerificationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;

        // Lower-cased exactly as registration stores it, so Alice@example.com finds the account
        // alice@example.com registered.
        string email = request.Email.Trim().ToLowerInvariant();

        // The account id is left empty on purpose: resolving an address to an account is a read
        // this layer must not perform, because doing it here and then inserting would be a
        // read-then-write check outside the transaction that arbitrates it. The write path stamps
        // the id it resolved, and says so on IUserRepository.
        IssuedVerificationToken issued = VerificationTokenIssuer.Issue(Guid.Empty, nowUtc);

        bool written = await _users
            .TryIssueEmailVerificationTokenAsync(email, issued.Token, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!written)
        {
            // An unknown address and an already-verified account send nothing and are answered 202
            // alike. A mail, or its absence being observable any other way, would tell an anonymous
            // caller which addresses hold a confirmed account — SPEC-004, owner decision 3.
            return;
        }

        // After the insert committed, never inside it — ADR-0013.
        await SendVerificationLinkAsync(email, issued.Plaintext, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendVerificationLinkAsync(
        string emailAddress,
        string plaintextToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mail
                .SendEmailVerificationAsync(emailAddress, plaintextToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed for the reason registration swallows it: letting this escape would turn an
            // unverified account into a 500 while an unknown address still answered 202, which is
            // the timing-free enumeration oracle this endpoint exists inside. The token is written,
            // so the caller can ask again.
            _logger.LogWarning(
                exception,
                "Verification mail for a resend request could not be sent. The token was issued.");
        }
    }
}
