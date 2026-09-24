using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Liveness and readiness, as specified in <c>docs/api/ENDPOINTS.md</c>.
/// Both are anonymous, and both sit outside <c>/api/v1</c> because they are operational surface
/// rather than product surface. They are declared as minimal API endpoints rather than through
/// <c>MapHealthChecks</c> so that they appear in the generated OpenAPI document — openapi-documentation
/// requires the document to describe every endpoint, and a health-check middleware route carries no
/// API description for the generator to read.
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>Maps <c>GET /health</c> and <c>GET /health/ready</c>.</summary>
    internal static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness touches no dependency on purpose: a liveness probe that fails when the database
        // is down asks an orchestrator to restart a process that is not the problem.
        endpoints.MapGet("/health", () => Results.Ok(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = HealthStatus.Healthy.ToString(),
        }))
        .AllowAnonymous()
        .WithName("GetHealth")
        .WithSummary("Liveness")
        .WithDescription("Returns 200 while the process is serving. Touches no dependency.")
        .Produces<Dictionary<string, string>>(StatusCodes.Status200OK);

        endpoints.MapGet("/health/ready", CheckReadinessAsync)
            .AllowAnonymous()
            .WithName("GetHealthReady")
            .WithSummary("Readiness")
            .WithDescription("Returns 200 when every dependency is reachable, 503 when one is not.")
            .Produces<Dictionary<string, string>>(StatusCodes.Status200OK)
            .Produces<Dictionary<string, string>>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// Runs the registered dependency checks. The body names each check and its state, and nothing
    /// else — no exception text, no host, no connection string, no server version.
    /// </summary>
    private static async Task<IResult> CheckReadinessAsync(
        HealthCheckService healthCheckService,
        CancellationToken cancellationToken)
    {
        HealthReport report = await healthCheckService
            .CheckHealthAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> body = new(StringComparer.Ordinal)
        {
            ["status"] = report.Status.ToString(),
        };

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            body[entry.Key] = entry.Value.Status.ToString();
        }

        return report.Status == HealthStatus.Unhealthy
            ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(body);
    }
}
