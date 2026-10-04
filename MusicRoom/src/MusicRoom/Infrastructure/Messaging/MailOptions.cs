namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// Which sender the host runs, and everything that sender needs — ADR-0013, SPEC-024.
/// </summary>
/// <param name="Provider">The value of <c>MAIL_PROVIDER</c>, lower-cased by the reader. <c>brevo</c> is the
/// hosted sender; <c>logging</c> is the Development-only sender that writes the link to the log.</param>
/// <param name="ApiKey">The provider's API key. Required by <c>brevo</c>, and never logged, never named in a
/// failure message and never part of an exception — <c>secrets-management</c>.</param>
/// <param name="FromAddress">The sender address. Brevo refuses a message whose sender is neither a verified
/// address nor its sandbox sender, so a wrong value here fails at send time rather than at start-up.</param>
/// <param name="FromName">The display name on the message. Optional; the provider shows the address when it
/// is absent.</param>
/// <param name="VerificationLinkBaseUrl">Where the client serves the verification screen. The token is
/// appended as a query parameter.</param>
/// <param name="PasswordResetLinkBaseUrl">Where the client serves the reset screen.</param>
/// <remarks>
/// <see cref="Validate"/> is what makes a misconfigured host fail at start-up instead of on its first
/// registration, which is the worst moment to discover a missing key: the account row is written and the
/// message is gone. The rule it enforces is deliberately not "is this Development?" but "is there a sender?" —
/// a typo in <c>MAIL_PROVIDER</c> must not be able to downgrade a production host to the sender that writes a
/// live credential into a log (T-26).
/// </remarks>
public sealed record MailOptions(
    string Provider,
    string? ApiKey,
    string? FromAddress,
    string? FromName,
    string? VerificationLinkBaseUrl,
    string? PasswordResetLinkBaseUrl)
{
    /// <summary>The hosted provider of ADR-0013.</summary>
    public const string BrevoProvider = "brevo";

    /// <summary>The Development-only sender that writes the link to the log.</summary>
    public const string LoggingProvider = "logging";

    /// <summary>
    /// How long one send may take before it is abandoned — SPEC-024 decision 4.
    /// </summary>
    /// <remarks>
    /// On the options rather than hidden in the registration, so the value a host runs with is visible where
    /// every other mail setting is. There is no retry behind it: the resend endpoint is the retry path and it
    /// is rate-limited, while a retry here would spend provider quota after the limiter has already passed the
    /// request (T-19).
    /// </remarks>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Throws unless this combination describes a sender that may run on this host.
    /// </summary>
    /// <param name="isDevelopment">True when the host environment is Development.</param>
    /// <exception cref="InvalidOperationException">Nothing valid is configured. The message names the
    /// variable that is missing and never its value.</exception>
    public void Validate(bool isDevelopment)
    {
        switch (Provider)
        {
            case BrevoProvider when string.IsNullOrWhiteSpace(ApiKey):
                throw new InvalidOperationException(
                    "MAIL_PROVIDER is brevo but MAIL_API_KEY is not set. Copy .env.example to .env and "
                    + "fill it in — see ADR-0013.");

            case BrevoProvider when string.IsNullOrWhiteSpace(FromAddress):
                throw new InvalidOperationException(
                    "MAIL_PROVIDER is brevo but MAIL_FROM is not set. Use a verified sender or Brevo's "
                    + "sandbox sender — see ADR-0013.");

            case BrevoProvider:
                return;

            // The Development sender writes the verification link and the reset token to the log, which is
            // why no other environment may have it — T-26, and the guard SPEC-003 introduced.
            case LoggingProvider when !isDevelopment:
                throw new InvalidOperationException(
                    "MAIL_PROVIDER is logging, which writes the verification link and the reset token to "
                    + "the application log and is allowed on a Development host only. Set MAIL_PROVIDER to "
                    + "brevo — see ADR-0013 and T-26.");

            case LoggingProvider:
                return;

            default:
                throw new InvalidOperationException(
                    "MAIL_PROVIDER must be 'brevo' or, on a Development host, 'logging'. No mail sender is "
                    + "registered without it, and the application does not start without a sender — "
                    + "ADR-0013, issue O17.");
        }
    }
}
