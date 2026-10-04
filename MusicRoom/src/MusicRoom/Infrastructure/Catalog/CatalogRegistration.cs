using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using MusicRoom.Application.Abstractions.Catalog;
using Polly;

namespace MusicRoom.Infrastructure.Catalog;

/// <summary>
/// Wires the catalog: the typed provider client, its resilience pipeline, and the cache in front — ADR-0003,
/// ADR-0023.
/// </summary>
/// <remarks>
/// Its own class rather than more lines in <c>InfrastructureServiceRegistration</c>, which is at the
/// five-argument limit `SonarLint.xml` enforces and already delegates its other adapters the same way.
/// </remarks>
internal static class CatalogRegistration
{
    /// <summary>Where the Data API answers.</summary>
    /// <remarks>Trailing slash: a relative request URI resolves against the base, and without it the last
    /// segment is dropped.</remarks>
    internal const string YouTubeBaseAddress = "https://www.googleapis.com/";

    /// <summary>How many times a failed call is retried before the caller is told the catalog is down.</summary>
    /// <remarks>
    /// Three attempts in total. `youtube-integration` allows a retry on `5xx` and `429` only, which the handler
    /// below is configured for — a `403 quotaExceeded` is **not** retried, because the quota does not come back
    /// within a request and retrying would spend the little that remains.
    /// </remarks>
    internal const int MaximumRetryAttempts = 2;

    /// <summary>Registers the provider, the cache and the memory level.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="options">The key and the lifetimes, already read from configuration.</param>
    internal static void Add(IServiceCollection services, CatalogOptions options)
    {
        services.AddSingleton(options);
        services.AddMemoryCache();

        services
            .AddHttpClient<ICatalogProvider, YouTubeCatalogProvider>(client =>
            {
                client.BaseAddress = new Uri(YouTubeBaseAddress);

                // The pipeline below owns every timeout. `HttpClient.Timeout` applies to the whole send,
                // retries included, so leaving it at the per-attempt value silently cancelled the last
                // attempt — found by `GetAsync_WhenTheProviderFails_IsUnavailableAfterBoundedRetriesAsync`.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("catalog", pipeline => pipeline

                // Outermost: how long a caller waits for the catalog to give up, retries and all.
                .AddTimeout(options.TotalTimeout)
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = MaximumRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,

                    // Short, because this sits in front of a user waiting for search results. The default
                    // two-second base would spend the whole budget on waiting rather than on attempts.
                    Delay = TimeSpan.FromMilliseconds(200),

                    // The default predicate is exactly the standard's rule: a 5xx or a 429 is retried, and no
                    // other 4xx is. A retried 403 would spend quota the provider has already refused to give.
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(response =>
                            response.StatusCode >= System.Net.HttpStatusCode.InternalServerError
                            || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                })

                // The breaker is why this package is here: without it a provider outage holds every request
                // thread for the timeout, and an event's own endpoints starve behind the catalog's.
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    FailureRatio = 0.5,
                    MinimumThroughput = 10,
                    BreakDuration = TimeSpan.FromSeconds(15),
                })
                // Innermost: one attempt's own limit, so a hung connection does not hold the budget.
                .AddTimeout(options.RequestTimeout));

        // Scoped, because L2 is a scoped repository. L1 is the singleton memory cache, so what survives a
        // request is the cache and not the decorator.
        services.AddScoped<ICatalog>(provider => new CachedCatalog(
            provider.GetRequiredService<ICatalogProvider>(),
            provider.GetRequiredService<Application.Abstractions.Persistence.ITrackRefRepository>(),
            provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            options,
            TimeProvider.System));
    }
}
