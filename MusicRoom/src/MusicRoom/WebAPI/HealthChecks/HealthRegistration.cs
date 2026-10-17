using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// The dependency set a reader is told about: the readiness probes, and the catalog's start-up notice.
/// </summary>
internal static class HealthRegistration
{
    /// <summary>Registers the dependency probes — the database, and the catalog's configuration.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="connectionString">What the database probe opens.</param>
    /// <param name="catalogConfigured">Whether a catalog provider key reached this host — SPEC-053.</param>
    /// <remarks>
    /// The catalog's failure status is <see cref="HealthStatus.Degraded" />, so a host without a provider
    /// key still reports ready: only search touches YouTube, and readiness is about whether this service can
    /// serve, not about whether a third party is well.
    /// </remarks>
    internal static void Add(IServiceCollection services, string connectionString, bool catalogConfigured)
    {
        // The start-up notice answers the same question as the `catalog` probe, for a reader who has the logs
        // rather than the endpoint — SPEC-053 decision 2. Registered here so the two cannot drift apart.
        services.AddHostedService<CatalogStartupNotice>();

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name: "database",
                factory: _ => new DatabaseReadinessCheck(connectionString),
                failureStatus: HealthStatus.Unhealthy,
                tags: null))
            .Add(new HealthCheckRegistration(
                name: "catalog",
                factory: _ => new CatalogConfigurationCheck(catalogConfigured),
                failureStatus: HealthStatus.Degraded,
                tags: null));
    }
}
