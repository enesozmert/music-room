using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// Sends a credential message and refuses to let its failure reach the caller — ADR-0013, SPEC-024.
/// </summary>
/// <remarks>
/// A dead mail provider costs a log line, not a `500`. That is what keeps registration's `202` from being
/// an enumeration oracle: the answer must not depend on whether a message went out, because the answer is
/// the same for an address that exists and one that does not.
/// <para>
/// **It also decides the language** — SPEC-052. The handlers that cause a message have no business reading
/// an HTTP header, and giving each of them an accessor would push three constructors past the five-argument
/// limit for one value they all resolve the same way. It is read here, once, from the client context the
/// audit behaviour already carries.
/// </para>
/// </remarks>
public sealed class CredentialMailDispatcher
{
    private readonly IMailService _mail;
    private readonly IClientContextAccessor _clientContext;
    private readonly ILogger<CredentialMailDispatcher> _logger;

    /// <summary>Takes the sender, the client context and a logger.</summary>
    /// <param name="mail">The mail sender.</param>
    /// <param name="clientContext">Where the caller's language comes from — SPEC-052.</param>
    /// <param name="logger">Where a swallowed failure is reported.</param>
    public CredentialMailDispatcher(
        IMailService mail,
        IClientContextAccessor clientContext,
        ILogger<CredentialMailDispatcher> logger)
    {
        _mail = mail;
        _clientContext = clientContext;
        _logger = logger;
    }

    /// <summary>Sends a verification message, or logs why it could not be sent.</summary>
    /// <param name="emailAddress">The recipient.</param>
    /// <param name="displayName">What to call them, or empty when the caller does not know — the resend
    /// path is keyed on an address and deliberately reads no account.</param>
    /// <param name="token">The token the link carries. Never logged here.</param>
    /// <param name="code">The six characters they may type instead. Never logged here.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the attempt is over, successful or not.</returns>
    public Task SendVerificationAsync(
        string emailAddress,
        string displayName,
        string token,
        string code,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _mail.SendEmailVerificationAsync(
                new VerificationMail(emailAddress, displayName, token, code, Language()),
                cancellationToken),
            "Verification mail could not be sent. The token was issued.");

    /// <summary>Sends a password reset message, or logs why it could not be sent.</summary>
    /// <param name="emailAddress">The recipient.</param>
    /// <param name="displayName">What to call them, or empty.</param>
    /// <param name="token">The reset token. Never logged here.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the attempt is over, successful or not.</returns>
    public Task SendPasswordResetAsync(
        string emailAddress,
        string displayName,
        string token,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _mail.SendPasswordResetAsync(
                new PasswordResetMail(emailAddress, displayName, token, Language()),
                cancellationToken),
            "Password reset mail could not be sent. The token was issued.");

    /// <summary>Sends the welcome message, or logs why it could not be sent.</summary>
    /// <param name="emailAddress">The recipient.</param>
    /// <param name="displayName">What to call them.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the attempt is over, successful or not.</returns>
    /// <remarks>
    /// Swallowed like the others, and for a sharper reason: this one is sent during a **sign-in**. A mail
    /// provider having a bad minute must not be able to refuse somebody their session.
    /// </remarks>
    public Task SendWelcomeAsync(
        string emailAddress,
        string displayName,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _mail.SendWelcomeAsync(
                new WelcomeMail(emailAddress, displayName, Language()),
                cancellationToken),
            "Welcome mail could not be sent. The account is signed in and stamped.");

    /// <summary>
    /// The language the caller asked for, or English when nothing did.
    /// </summary>
    /// <remarks>
    /// The context is absent on a path with no HTTP request behind it — a background send, or a test that
    /// did not set one. English rather than a throw: a message in the wrong language is a worse message,
    /// and no message at all is a worse outcome than that.
    /// </remarks>
    private MailLanguage Language() => _clientContext.Current?.Language ?? MailLanguage.English;

    private async Task SendAsync(Func<Task> send, string failureMessage)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Swallowed on purpose — ADR-0013, and the enumeration reasoning in the remarks above. The
            // credential is written, so the caller can ask again.
            _logger.LogWarning(exception, "{FailureMessage}", failureMessage);
        }
    }
}
