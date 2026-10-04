using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MusicRoom.WebAPI.RateLimiting;

/// <summary>
/// The per-IP rate limit policies in front of the credential endpoints — SDD §13.3, SPEC-006.
/// </summary>
/// <remarks>
/// Per IP only. The per-account half is <c>ICredentialAttemptLimiter</c>, enforced inside the handlers,
/// because this middleware runs before model binding and finding the submitted address here would mean
/// parsing untrusted JSON in the component whose job is to bound work (SPEC-006 escalation 2, option B).
/// <para>
/// State is in process, which is correct for the single instance ADR-0015 deploys and would not be
/// shared across several — issue O8.
/// </para>
/// </remarks>
internal static class CredentialRateLimiterPolicies
{
    /// <summary>Signing in.</summary>
    internal const string Sessions = "credential-sessions";

    /// <summary>Refresh rotation.</summary>
    internal const string Tokens = "credential-tokens";

    /// <summary>Registration.</summary>
    internal const string Registrations = "credential-registrations";

    /// <summary>Verification resend.</summary>
    internal const string VerificationRequests = "credential-verification-requests";

    /// <summary>Email verification.</summary>
    internal const string EmailVerifications = "credential-email-verifications";

    /// <summary>Asking for a password reset link.</summary>
    internal const string PasswordResetRequests = "credential-password-reset-requests";

    /// <summary>Spending a password reset token.</summary>
    internal const string PasswordResets = "credential-password-resets";

    /// <summary>Changing a password while signed in — SPEC-028.</summary>
    internal const string PasswordChanges = "credential-password-changes";

    /// <summary>Registers every credential policy and the shared rejection behaviour.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the approved numbers may be overridden.</param>
    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        CredentialRateLimitOptions options = CredentialRateLimitOptions.Read(configuration);

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(Sessions, context => PartitionByRemoteIp(context, options.Sessions));
            limiter.AddPolicy(Tokens, context => PartitionByRemoteIp(context, options.Tokens));
            limiter.AddPolicy(Registrations, context => PartitionByRemoteIp(context, options.Registrations));
            limiter.AddPolicy(
                VerificationRequests,
                context => PartitionByRemoteIp(context, options.VerificationRequests));
            limiter.AddPolicy(
                EmailVerifications,
                context => PartitionByRemoteIp(context, options.EmailVerifications));

            limiter.AddPolicy(
                PasswordResetRequests,
                context => PartitionByRemoteIp(context, options.PasswordResetRequests));
            limiter.AddPolicy(
                PasswordResets,
                context => PartitionByRemoteIp(context, options.PasswordResets));
            limiter.AddPolicy(
                PasswordChanges,
                context => PartitionByRemoteIp(context, options.PasswordChanges));

            limiter.OnRejected = OnRejectedAsync;
        });
    }

    /// <summary>
    /// Answers `429` with <c>Retry-After</c> and an empty body, and logs the trip.
    /// </summary>
    /// <remarks>
    /// No body on purpose: a ProblemDetails naming which partition tripped tells a caller which key to
    /// vary. The log line carries the policy and the wait and never the address or the IP — SPEC-006
    /// escalation 4, and the never-logged list in `application-security`.
    /// </remarks>
    private static ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext httpContext = context.HttpContext;

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(CredentialRateLimiterPolicies))
            .LogWarning(
                "Rate limit tripped on {Endpoint} by the per-IP partition. Retry after {RetryAfterSeconds}s.",
                httpContext.Request.Path.Value,
                (int)Math.Ceiling(retryAfter.TotalSeconds));

        return ValueTask.CompletedTask;
    }

    private static RateLimitPartition<string> PartitionByRemoteIp(
        HttpContext httpContext,
        CredentialRateLimitWindow window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            // An absent remote address shares one bucket rather than getting its own: an unkeyable
            // caller must not be the cheapest caller.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = window.PermitLimit,
                Window = window.Window,
                AutoReplenishment = true,
            });
}
