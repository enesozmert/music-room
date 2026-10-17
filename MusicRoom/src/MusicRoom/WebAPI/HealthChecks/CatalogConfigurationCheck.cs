using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// Reports whether this host has a catalog provider — SPEC-053 decisions 3 and 4.
/// </summary>
/// <remarks>
/// **It does not call YouTube.** A `search.list` costs 100 of the 10,000 daily quota units (ADR-0023), so a
/// probe that reached the provider would spend the quota it exists to watch — and on a platform that wakes a
/// sleeping container per request, it would do so at a rate nobody chose. What it answers is a question with
/// no network in it: is a key configured.
/// <para>
/// **It is `Degraded`, never `Unhealthy`.** Signing in, profiles, playlists, events, votes, devices and
/// playback touch no provider; only search does. A readiness probe that failed here would take this service
/// out of rotation for somebody else's outage — or, as happened on 2026-10-17, for a variable nobody set.
/// </para>
/// <para>
/// This check is why that variable is now visible: `YOUTUBE_API_KEY` was missing on the deployment, every
/// search answered `503`, and the only way to find out was to ask.
/// </para>
/// </remarks>
internal sealed class CatalogConfigurationCheck : IHealthCheck
{
    private readonly bool _configured;

    /// <summary>Takes the answer, which is decided once at start-up and cannot change while running.</summary>
    /// <param name="configured">Whether a provider key reached this host.</param>
    internal CatalogConfigurationCheck(bool configured) => _configured = configured;

    /// <inheritdoc />
    /// <remarks>
    /// The description says configured or not and nothing else: not the key, not its length, not its first
    /// characters. A health endpoint is anonymous — `secrets-management`.
    /// </remarks>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_configured
            ? HealthCheckResult.Healthy("configured")
            : HealthCheckResult.Degraded("no provider configured; search answers 503"));
}
