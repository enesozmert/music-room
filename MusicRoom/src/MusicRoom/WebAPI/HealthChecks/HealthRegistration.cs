using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicRoom.Infrastructure.Catalog;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// The dependency set a reader is told about: the readiness probes, and the start-up notices beside them.
/// </summary>
internal static class HealthRegistration
{
    /// <summary>Registers the dependency probes and the notices that answer the same questions.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the optional dependencies' state is read from — the catalog's key
    /// (SPEC-053) and the social provider rows (SPEC-056). Read here rather than in `Main`, which is at
    /// CA1506's coupling limit and gained a type it did not need to know about.</param>
    /// <param name="connectionString">What the database probe opens.</param>
    /// <remarks>
    /// **Only the database can make this service unready.** The catalog and the social providers are
    /// <see cref="HealthStatus.Degraded" />, because the features behind them are one route each while the
    /// rest of the product serves without them. Readiness is about whether this service can serve, not
    /// about whether a third party is well — and a probe that failed for a third party would take a
    /// working service out of rotation.
    /// <para>
    /// One method for both optional dependencies, so a probe and the start-up line that answers the same
    /// question cannot drift apart — SPEC-056 decision 5.
    /// </para>
    /// </remarks>
    internal static void Add(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        bool catalogConfigured = EnvironmentConfiguration.ReadCatalogOptions(configuration).HasProvider;

        IReadOnlyList<string> socialProviders =
            [.. SocialProviderConfiguration.Read(configuration).Select(row => row.Name)];

        // The start-up notices are for the reader who has the logs rather than the endpoint: the answer is
        // in the first screen after a deploy, before anybody reproduces anything.
        services.AddHostedService<CatalogStartupNotice>();
        services.AddSingleton<IHostedService>(provider => new SocialStartupNotice(
            provider.GetRequiredService<ILogger<SocialStartupNotice>>(),
            socialProviders));

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
                tags: null))
            .Add(new HealthCheckRegistration(
                name: "social",
                factory: _ => new SocialConfigurationCheck(socialProviders),
                failureStatus: HealthStatus.Degraded,
                tags: null));
    }
}
