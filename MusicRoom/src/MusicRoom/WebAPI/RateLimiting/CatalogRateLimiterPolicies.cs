using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MusicRoom.WebAPI.RateLimiting;

/// <summary>
/// The limit on the one route that can reach `search.list` — SPEC-025 decision 8.
/// </summary>
/// <remarks>
/// Partitioned by the validated account rather than the address, like the event policies: a room full of people
/// searching over one venue's network must not share an allowance, which is the defect SPEC-023 found in the
/// vote limiter and ADR-0040 fixed.
/// <para>
/// `GET /tracks/{id}` carries no policy. It prefers both caches and costs one quota unit when they miss, so
/// limiting it would protect nothing and would break a page that legitimately resolves twenty rows.
/// </para>
/// <para>
/// State is in process, as for every other limiter here — issue O8.
/// </para>
/// </remarks>
internal static class CatalogRateLimiterPolicies
{
    /// <summary>Searching the catalog.</summary>
    internal const string Searches = "catalog-searches";

    /// <summary>
    /// How many searches one account may run per minute.
    /// </summary>
    /// <remarks>
    /// Thirty a minute: far above a person typing against a debounced client, far below a script. The reasoning
    /// is the vote limit's (SPEC-018) — this bounds abuse of a quota the whole project shares, and one search
    /// costs about a hundred times a metadata read.
    /// </remarks>
    internal const int SearchPermits = 30;

    /// <summary>Registers the policy.</summary>
    /// <param name="services">The container being built.</param>
    /// <remarks>
    /// `AddRateLimiter` is additive, so this sits beside the credential and event policies rather than replacing
    /// them, and the shared `OnRejected` they registered — the bodiless `429` with `Retry-After` — covers this
    /// one too.
    /// </remarks>
    internal static void Add(IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
            limiter.AddPolicy(Searches, PartitionByAccount));

    private static RateLimitPartition<string> PartitionByAccount(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AccountKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = SearchPermits,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            });

    private static string AccountKey(HttpContext httpContext)
    {
        string? subject = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        // An unauthenticated caller falls back to the address, so an unkeyable caller is not the cheapest
        // caller. The route requires authorisation, so this is the belt rather than the braces.
        return string.IsNullOrWhiteSpace(subject)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : subject;
    }
}
