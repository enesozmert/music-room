namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// Says once, at start-up, whether this host can sign anybody in through a provider — SPEC-056.
/// </summary>
/// <remarks>
/// The reason this exists: the four `GOOGLE_*` variables were never set on the live deployment, so every
/// social sign-in answered `422 PROVIDER.UNKNOWN`, and the way it came to light was the front-end team
/// reporting it as a defect in the API. A refusal to a client is the wrong place for a deployment to
/// learn about its own configuration.
/// <para>
/// A start-up line is the right place. The set of providers is decided once, before the first request,
/// and cannot change while the process runs. It names the variables, because the fix is to set them,
/// and never a value — `secrets-management`.
/// </para>
/// </remarks>
internal sealed class SocialStartupNotice : IHostedService
{
    private readonly ILogger<SocialStartupNotice> _logger;
    private readonly IReadOnlyList<string> _providers;

    /// <summary>Takes the logger and what configuration produced.</summary>
    /// <param name="logger">The application logger.</param>
    /// <param name="providers">The provider names, which may be empty.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public SocialStartupNotice(ILogger<SocialStartupNotice> logger, IReadOnlyList<string> providers)
    {
        _logger = logger;
        _providers = providers;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_providers.Count > 0)
        {
            _logger.LogInformation(
                "Social sign-in configured for {SocialProviders}.",
                string.Join(", ", _providers));

            return Task.CompletedTask;
        }

        // All four together, because a row with three of them is no row: `SocialProviderConfiguration`
        // returns an empty set rather than a half-configured provider.
        _logger.LogWarning(
            "No social provider configured on this host: GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, "
            + "GOOGLE_DISCOVERY_URL and GOOGLE_ISSUER are required together, and at least one is absent. "
            + "POST /api/v1/auth/sessions/social answers 422 PROVIDER.UNKNOWN until all four are set. "
            + "Password sign-in and every other feature are unaffected, so readiness still reports ready.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Nothing to stop: this service only ever wrote one line.</remarks>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
