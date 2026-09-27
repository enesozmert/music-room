using MediatR;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.Register;

/// <summary>
/// Hashes the password, writes the account, issues its verification token and mails the link —
/// SDD §10.1 steps 3 and 4.
/// </summary>
internal sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMailService _mail;
    private readonly ILogger<RegisterCommandHandler> _logger;

    /// <summary>Takes the write path, the hasher and the mail sender; all abstractions this layer declares.</summary>
    public RegisterCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IMailService mail,
        ILogger<RegisterCommandHandler> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _mail = mail;
        _logger = logger;
    }

    /// <summary>Registers the account, or does nothing visible when the address is already taken.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTime nowUtc = DateTime.UtcNow;

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
            return;
        }

        // After the insert committed, never inside it — ADR-0013.
        await SendVerificationLinkAsync(user.Email, issued.Plaintext, cancellationToken).ConfigureAwait(false);
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
            // Swallowed on purpose, and the reason is not just ADR-0013's "a failed send must not
            // roll back registration". Letting this escape would turn a fresh address into a 500
            // while a duplicate still answered 202 — the timing-free enumeration oracle this whole
            // flow is built to avoid. The account exists; /auth/verification-requests resends.
            _logger.LogWarning(
                exception,
                "Verification mail for a new registration could not be sent. The account was created.");
        }
    }
}
