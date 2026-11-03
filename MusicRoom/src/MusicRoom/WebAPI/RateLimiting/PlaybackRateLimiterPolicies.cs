using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MusicRoom.WebAPI.RateLimiting;

/// <summary>
/// The limit on the one route a timer drives — SPEC-059 decision 5.
/// </summary>
/// <remarks>
/// Every other write in this system is paced by a person pressing something. A position report is paced
/// by a clock in the playing device: every fifteen seconds while the music plays, plus one on each play,
/// pause and seek. The limit is what keeps a client with a wrong interval, or a loop, from turning that
/// into a write storm.
/// <para>
/// **Partitioned by the session, not the account.** One party's speaker must not spend the allowance of
/// somebody running two rooms, and the thing being protected is the row behind one session. This is the
/// opposite choice from the catalog and vote limiters, which partition by account because what they
/// protect is a shared quota — ADR-0040.
/// </para>
/// <para>
/// One a second is four times what the client sends. Above that is a client that is wrong, and the
/// mobile side already treats a refusal as something to drop rather than retry; the `429` with
/// `Retry-After` is that, said out loud.
/// </para>
/// <para>
/// State is in process, as for every other limiter here — issue O8.
/// </para>
/// </remarks>
internal static class PlaybackRateLimiterPolicies
{
    /// <summary>Reporting where the playing device is.</summary>
    internal const string Positions = "playback-positions";

    /// <summary>How many reports one session may send per second, unless configuration says otherwise.</summary>
    internal const int PositionPermits = 1;

    /// <summary>The variable that raises it — the same shape the credential limits use.</summary>
    internal const string PermitsKey = "RATE_LIMIT_PLAYBACK_POSITION_PERMITS";

    /// <summary>Registers the policy.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where a deployment may raise the permit count from its default of one.</param>
    /// <remarks>
    /// `AddRateLimiter` is additive, so this sits beside the credential, event and catalog policies and
    /// inherits the shared `OnRejected` they registered — the bodiless `429` with `Retry-After`.
    /// <para>
    /// The count is configurable for the same reason the credential limits are: a suite that drives fifty
    /// reports through this route in a second is testing the route, and a limiter it cannot raise would
    /// make it test the limiter instead. The default is the production number.
    /// </para>
    /// </remarks>
    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int permits = configuration.GetValue(PermitsKey, PositionPermits);

        services.AddRateLimiter(limiter =>
            limiter.AddPolicy(Positions, httpContext => PartitionBySession(httpContext, permits)));
    }

    private static RateLimitPartition<string> PartitionBySession(HttpContext httpContext, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: SessionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
            });

    /// <summary>The session id out of the route, or the address when the route has none.</summary>
    /// <remarks>
    /// The fallback cannot happen through the mapped route, which declares the segment. It exists so that
    /// an unkeyable request is not the cheapest request in the system.
    /// </remarks>
    private static string SessionKey(HttpContext httpContext) =>
        httpContext.Request.RouteValues.TryGetValue("sessionId", out object? sessionId)
        && sessionId is string key
            ? key
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
