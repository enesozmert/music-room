using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// Writes the verification link to the structured log instead of sending a message.
/// </summary>
/// <remarks>
/// This exists because O17 has no hosted provider yet, and a developer has to be able to read the
/// link out of <c>docker compose logs</c> and finish the flow. It is registered in Development only
/// — <see cref="InfrastructureServiceRegistration"/> fails start-up in any other environment rather
/// than falling back to it. The residual risk, a live credential in a log, is recorded as T-26 in
/// <c>docs/owasp/THREAT-REGISTER.md</c> and closes when slice S1a lands the real sender.
/// </remarks>
internal sealed class LoggingMailService : IMailService
{
    private readonly ILogger<LoggingMailService> _logger;
    private readonly string? _verificationLinkBaseUrl;

    /// <summary>Takes the logger and the base URL the link is composed from.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="verificationLinkBaseUrl">Where the client serves the verification screen, from
    /// configuration. Null or blank when nothing configured it: this class composes no default,
    /// because a link pointing at a machine nobody intended is worse than no link.</param>
    /// <remarks>
    /// Public on an internal type: the container activates the registered implementation through a
    /// public constructor only.
    /// </remarks>
    public LoggingMailService(ILogger<LoggingMailService> logger, string? verificationLinkBaseUrl)
    {
        _logger = logger;
        _verificationLinkBaseUrl = verificationLinkBaseUrl;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Both credentials are written, because either completes the flow and a developer should not have to
    /// guess which one this build issued — SPEC-052.
    /// </remarks>
    public Task SendEmailVerificationAsync(VerificationMail message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Email verification issued for {EmailAddress} in {MailLanguage}: code {VerificationCode}, "
            + "token {VerificationToken}. {VerificationLink}",
            message.EmailAddress,
            message.Language,
            message.Code,
            message.Token,
            Link(message.Token));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The reset token is written to the log, which the repository owner accepted on 2026-09-27
    /// (SPEC-006 escalation 3 — recorded as the widening of T-26). It is strictly worse than the
    /// verification credential this class already logs: a reader of a Development log can take the account
    /// over, not merely confirm its address. The start-up guard in
    /// <see cref="InfrastructureServiceRegistration"/> is what keeps that to Development hosts.
    /// </remarks>
    public Task SendPasswordResetAsync(PasswordResetMail message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning(
            "Password reset token issued for {EmailAddress} in {MailLanguage}: {PasswordResetToken}. This "
            + "token replaces a password; it is logged only because this sender is Development-only (T-26).",
            message.EmailAddress,
            message.Language,
            message.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Nothing secret in this one, so it is an ordinary information line.</remarks>
    public Task SendWelcomeAsync(WelcomeMail message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Welcome mail issued for {EmailAddress} in {MailLanguage}.",
            message.EmailAddress,
            message.Language);

        return Task.CompletedTask;
    }

    /// <summary>The link, or a sentence saying why there is none.</summary>
    private string Link(string token) =>
        string.IsNullOrWhiteSpace(_verificationLinkBaseUrl)
            ? "VERIFICATION_LINK_BASE_URL is not set, so no link was composed."
            : $"{_verificationLinkBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(token)}";
}
