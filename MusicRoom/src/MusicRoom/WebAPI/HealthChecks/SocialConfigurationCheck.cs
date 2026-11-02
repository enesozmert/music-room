using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// Reports which social providers this host is configured for — SPEC-056.
/// </summary>
/// <remarks>
/// **It does not call the provider.** Fetching Google's discovery document from a readiness probe would
/// import a third party's outage into this service's readiness, and on a tier that wakes a sleeping
/// instance per request it would fetch it at a rate nobody chose. What it answers has no network in it:
/// which provider rows configuration produced.
/// <para>
/// **It is `Degraded`, never `Unhealthy`.** Password sign-in, every product surface and the OpenID
/// provider work without a social provider — ADR-0008's amendment allows a host with none, and SPEC-041
/// built it that way. A readiness failure here would take a working service out of rotation.
/// </para>
/// <para>
/// **It names providers, never credentials.** `google` is the string a client puts in a request body, so
/// it is public by construction. A client id is not, and neither is a fragment of one — `secrets-management`.
/// </para>
/// <para>
/// This check is why the variables are now visible: the four `GOOGLE_*` values were never set on the live
/// deployment, every sign-in answered `422 PROVIDER.UNKNOWN`, and the front-end team reported it as an API
/// defect because nothing else could have told them.
/// </para>
/// </remarks>
internal sealed class SocialConfigurationCheck : IHealthCheck
{
    private readonly IReadOnlyList<string> _providers;

    /// <summary>Takes the provider names, decided once at start-up.</summary>
    /// <param name="providers">What configuration produced. May be empty.</param>
    internal SocialConfigurationCheck(IReadOnlyList<string> providers) => _providers = providers;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_providers.Count > 0
            ? HealthCheckResult.Healthy(string.Join(", ", _providers))
            : HealthCheckResult.Degraded("no social provider configured; social sign-in answers 422"));
}
