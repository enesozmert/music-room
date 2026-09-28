using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// Sends a credential mail and swallows a send failure — ADR-0013.
/// </summary>
/// <remarks>
/// Extracted from the three handlers that each had their own copy when SPEC-009 added the audit context and
/// pushed two of those constructors to six parameters, which `SonarLint.xml` refuses (S107). One copy is also
/// the right number for the reasoning involved: letting a send failure escape would turn an existing account
/// into a `500` while an unknown address still answered `202`, which is the timing-free enumeration oracle
/// these endpoints are built to avoid.
/// </remarks>
public sealed class CredentialMailDispatcher
{
    private readonly IMailService _mail;
    private readonly ILogger<CredentialMailDispatcher> _logger;

    /// <summary>Takes the sender and a logger.</summary>
    /// <param name="mail">The mail sender.</param>
    /// <param name="logger">Where a swallowed failure is reported.</param>
    public CredentialMailDispatcher(IMailService mail, ILogger<CredentialMailDispatcher> logger)
    {
        _mail = mail;
        _logger = logger;
    }

    /// <summary>Sends a verification link, or logs why it could not be sent.</summary>
    /// <param name="emailAddress">The recipient.</param>
    /// <param name="plaintextToken">The token the recipient clicks. Never logged here.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the attempt is over, successful or not.</returns>
    public Task SendVerificationAsync(
        string emailAddress,
        string plaintextToken,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _mail.SendEmailVerificationAsync(emailAddress, plaintextToken, cancellationToken),
            "Verification mail could not be sent. The token was issued.");

    /// <summary>Sends a password reset link, or logs why it could not be sent.</summary>
    /// <param name="emailAddress">The recipient.</param>
    /// <param name="plaintextToken">The token the recipient submits. Never logged here.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the attempt is over, successful or not.</returns>
    public Task SendPasswordResetAsync(
        string emailAddress,
        string plaintextToken,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _mail.SendPasswordResetAsync(emailAddress, plaintextToken, cancellationToken),
            "Password reset mail could not be sent. The token was issued.");

    private async Task SendAsync(Func<Task> send, string failureMessage)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed on purpose — ADR-0013, and the enumeration reasoning in the remarks above. The token
            // is written, so the caller can ask again.
            _logger.LogWarning(exception, "{FailureMessage}", failureMessage);
        }
    }
}
