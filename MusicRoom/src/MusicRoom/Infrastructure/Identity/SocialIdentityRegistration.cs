using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Identity;
using Polly;

namespace MusicRoom.Infrastructure.Identity;

/// <summary>
/// Wires the relying-party core and the provider rows it reads — SPEC-041.
/// </summary>
/// <remarks>
/// Delegated out of `InfrastructureServiceRegistration` for the reason the catalog and the mail sender are:
/// that method is at S107's argument limit, and a provider concern belongs with its own wiring.
/// <para>
/// The resilience pipeline is deliberately shorter than the catalog's. A provider's discovery document and
/// JWKS are fetched rarely and cached; the code exchange happens once per sign-in, with a person waiting. One
/// retry on a transient failure, and a timeout that keeps a provider outage from becoming this API's outage.
/// </para>
/// </remarks>
internal static class SocialIdentityRegistration
{
    /// <summary>How long a sign-in waits for a provider, attempts included.</summary>
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Registers the core with whatever rows configuration provided.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="providers">The configured rows. Empty is allowed: no provider is configured yet.</param>
    /// <remarks>
    /// An empty set registers the core anyway, and every request to it answers `UnknownProvider`. The
    /// alternative — refusing to start — would make a host without Google credentials unbootable, and S1's
    /// password sign-in does not need them.
    /// </remarks>
    internal static void Add(IServiceCollection services, IReadOnlyList<SocialProviderOptions> providers)
    {
        Dictionary<string, SocialProviderOptions> rows = providers.ToDictionary(
            row => row.Name,
            StringComparer.OrdinalIgnoreCase);

        services
            .AddHttpClient<ISocialIdentityProvider>(client =>

                // The pipeline owns the timeout: `HttpClient.Timeout` applies to the whole send, retries
                // included, which silently cancelled the last attempt in the catalog adapter — SPEC-025.
                client.Timeout = Timeout.InfiniteTimeSpan)
            .AddTypedClient<ISocialIdentityProvider>((client, provider) =>
                new OpenIdConnectIdentityProvider(
                    client,
                    rows,
                    provider.GetRequiredService<ILogger<OpenIdConnectIdentityProvider>>()))
            .AddResilienceHandler("social-identity", pipeline => pipeline
                .AddTimeout(TotalTimeout)
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.FromMilliseconds(200),
                    UseJitter = true,
                })
                .AddTimeout(TimeSpan.FromSeconds(4)));
    }
}
