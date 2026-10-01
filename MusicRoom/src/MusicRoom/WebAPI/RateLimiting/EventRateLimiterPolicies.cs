using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MusicRoom.WebAPI.RateLimiting;

/// <summary>
/// The per-account rate limits on the contended event routes — SPEC-018, ADR-0031's posture.
/// </summary>
/// <remarks>
/// Partitioned by the validated token's `sub`, not by IP: these routes are authenticated, and a room full of
/// people voting over one venue's network must not share a bucket. An unauthenticated request falls back to the
/// address, so an unkeyable caller is not the cheapest caller.
/// <para>
/// State is in process, which is correct for the single instance ADR-0015 deploys and would not be shared
/// across several — issue O8, as for every other limiter here.
/// </para>
/// </remarks>
internal static class EventRateLimiterPolicies
{
    /// <summary>Casting a vote.</summary>
    internal const string Votes = "event-votes";

    /// <summary>Minting a venue code.</summary>
    internal const string Codes = "event-codes";

    /// <summary>How many votes one account may cast per window.</summary>
    /// <remarks>
    /// Sixty a minute is far above what a person does and far below what a script does. A vote is cheap, so this
    /// bounds abuse rather than use: the thing being protected is the queue's meaning, not the server.
    /// </remarks>
    internal const int VotePermits = 60;

    /// <summary>How many codes one owner may mint per window.</summary>
    /// <remarks>
    /// Ten in five minutes. A code is credential-shaped, and a route that mints credentials without a limit is a
    /// route that mints them in bulk.
    /// </remarks>
    internal const int CodePermits = 10;

    /// <summary>Registers both policies.</summary>
    /// <param name="services">The container being built.</param>
    /// <remarks>
    /// `AddRateLimiter` is additive, so this sits beside the credential policies rather than replacing them, and
    /// the shared `OnRejected` those registered — the bodiless `429` with `Retry-After` — covers these too.
    /// </remarks>
    internal static void Add(IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(
                Votes,
                context => PartitionByAccount(context, VotePermits, TimeSpan.FromMinutes(1)));

            limiter.AddPolicy(
                Codes,
                context => PartitionByAccount(context, CodePermits, TimeSpan.FromMinutes(5)));
        });

    private static RateLimitPartition<string> PartitionByAccount(
        HttpContext httpContext,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AccountKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                AutoReplenishment = true,
            });

    /// <summary>
    /// The validated account, or the address when there is none.
    /// </summary>
    /// <remarks>
    /// The claim is read and not trusted beyond what the bearer handler already verified: by the time the
    /// limiter runs, `UseAuthentication` has checked the signature, the issuer, the audience and the lifetime.
    /// </remarks>
    private static string AccountKey(HttpContext httpContext)
    {
        string? subject = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(subject)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : subject;
    }
}
