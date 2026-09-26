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
    public Task SendEmailVerificationAsync(
        string emailAddress,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_verificationLinkBaseUrl))
        {
            _logger.LogInformation(
                "Email verification token issued for {EmailAddress}: {VerificationToken}. "
                + "VERIFICATION_LINK_BASE_URL is not set, so no link was composed.",
                emailAddress,
                token);

            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Email verification link issued for {EmailAddress}: {VerificationLink}",
            emailAddress,
            $"{_verificationLinkBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(token)}");

        return Task.CompletedTask;
    }
}
