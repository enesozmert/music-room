namespace MusicRoom.WebAPI.RateLimiting;

/// <summary>
/// The per-IP permits and windows the owner approved on 2026-09-27 — SPEC-006 escalation 1.
/// </summary>
/// <remarks>
/// The defaults <em>are</em> the decision, so a host with no override still limits at the approved
/// numbers: a limiter that fails to start, or starts wide open because a variable was missing, is worse
/// than one that defaults to what was signed off. Configuration may tighten or loosen them, which is
/// what lets a test host raise a limit for a suite that is proving something else.
/// <para>
/// One policy per endpoint rather than one shared policy: a shared bucket would let a registration loop
/// spend the sign-in allowance.
/// </para>
/// </remarks>
internal sealed class CredentialRateLimitOptions
{
    /// <summary>Signing in: 20 attempts per 5 minutes from one address.</summary>
    internal CredentialRateLimitWindow Sessions { get; init; } = new(20, TimeSpan.FromMinutes(5));

    /// <summary>Rotation: 60 per 5 minutes. Higher, because a legitimate client rotates on a schedule.</summary>
    internal CredentialRateLimitWindow Tokens { get; init; } = new(60, TimeSpan.FromMinutes(5));

    /// <summary>Registration: 10 per hour. Each success writes a row and sends a mail.</summary>
    internal CredentialRateLimitWindow Registrations { get; init; } = new(10, TimeSpan.FromHours(1));

    /// <summary>Verification resend: 10 per hour. This is the endpoint T-19's quota loop runs at.</summary>
    internal CredentialRateLimitWindow VerificationRequests { get; init; } = new(10, TimeSpan.FromHours(1));

    /// <summary>Verification: 30 per 5 minutes, which bounds token guessing without breaking a retry.</summary>
    internal CredentialRateLimitWindow EmailVerifications { get; init; } = new(30, TimeSpan.FromMinutes(5));

    /// <summary>
    /// Reset requests: 10 per hour, the verification-resend numbers. Same work, same cost — SPEC-007
    /// escalation 2.
    /// </summary>
    internal CredentialRateLimitWindow PasswordResetRequests { get; init; } = new(10, TimeSpan.FromHours(1));

    /// <summary>Reset: 30 per 5 minutes, which bounds token guessing without breaking a retry.</summary>
    internal CredentialRateLimitWindow PasswordResets { get; init; } = new(30, TimeSpan.FromMinutes(5));

    /// <summary>
    /// Reads the options from configuration, falling back to the approved numbers value by value.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The options the policies are built from.</returns>
    internal static CredentialRateLimitOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        CredentialRateLimitOptions defaults = new();

        return new CredentialRateLimitOptions
        {
            Sessions = ReadWindow(configuration, "RATE_LIMIT_SESSIONS", defaults.Sessions),
            Tokens = ReadWindow(configuration, "RATE_LIMIT_TOKENS", defaults.Tokens),
            Registrations = ReadWindow(configuration, "RATE_LIMIT_REGISTRATIONS", defaults.Registrations),
            VerificationRequests = ReadWindow(
                configuration,
                "RATE_LIMIT_VERIFICATION_REQUESTS",
                defaults.VerificationRequests),
            EmailVerifications = ReadWindow(
                configuration,
                "RATE_LIMIT_EMAIL_VERIFICATIONS",
                defaults.EmailVerifications),
            PasswordResetRequests = ReadWindow(
                configuration,
                "RATE_LIMIT_PASSWORD_RESET_REQUESTS",
                defaults.PasswordResetRequests),
            PasswordResets = ReadWindow(
                configuration,
                "RATE_LIMIT_PASSWORD_RESETS",
                defaults.PasswordResets),
        };
    }

    private static CredentialRateLimitWindow ReadWindow(
        IConfiguration configuration,
        string prefix,
        CredentialRateLimitWindow approved)
    {
        int permits = configuration.GetValue($"{prefix}_PERMITS", approved.PermitLimit);
        int seconds = configuration.GetValue($"{prefix}_WINDOW_SECONDS", (int)approved.Window.TotalSeconds);

        // A non-positive override would disable the limit, which is the one outcome configuration must
        // not be able to produce. Fall back to the approved value instead of trusting it.
        return permits <= 0 || seconds <= 0
            ? approved
            : new CredentialRateLimitWindow(permits, TimeSpan.FromSeconds(seconds));
    }
}

/// <summary>
/// One endpoint's per-IP allowance.
/// </summary>
/// <param name="PermitLimit">Requests allowed inside the window.</param>
/// <param name="Window">The window they are counted in.</param>
/// <remarks>
/// Declared beside the options rather than in its own file: it exists only as their shape, and
/// SPEC-006's Files table is the contract for what this change may create.
/// </remarks>
internal sealed record CredentialRateLimitWindow(int PermitLimit, TimeSpan Window);
