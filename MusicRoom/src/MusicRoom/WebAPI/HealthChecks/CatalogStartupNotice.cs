using MusicRoom.Infrastructure.Catalog;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// Says once, at start-up, whether this host can reach the catalog at all — SPEC-053 decision 2.
/// </summary>
/// <remarks>
/// The reason this exists: on 2026-10-17 the live deployment answered `503` to every search because
/// `YOUTUBE_API_KEY` had not been set on it. Nothing in the logs said so. The provider logs what it
/// does, so a missing key — the one case where it does nothing — was the one case that was silent.
/// <para>
/// A start-up line is the right place for it. The condition is decided once, before the first request,
/// and it cannot change while the process runs, so repeating it per request would say nothing new.
/// The provider still logs per call, for the operator who reads the logs after the fact rather than at boot.
/// </para>
/// <para>
/// It names the variable and never its value — `secrets-management`.
/// </para>
/// </remarks>
internal sealed class CatalogStartupNotice : IHostedService
{
    private readonly ILogger<CatalogStartupNotice> _logger;
    private readonly CatalogOptions _options;

    /// <summary>Takes the logger and the options the container already holds.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="options">The key and the lifetimes, read from configuration at start-up.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public CatalogStartupNotice(ILogger<CatalogStartupNotice> logger, CatalogOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.HasProvider)
        {
            // What a call costs is the provider's own line, per call, in quota units — it knows which call
            // it made and this does not.
            _logger.LogInformation("Catalog provider configured. Track search is available.");

            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "No catalog provider configured: YOUTUBE_API_KEY is not set on this host. "
            + "Track search and track lookup will answer 503 until it is. "
            + "Every other feature is unaffected, so readiness still reports ready.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Nothing to stop: this service only ever wrote one line.</remarks>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
