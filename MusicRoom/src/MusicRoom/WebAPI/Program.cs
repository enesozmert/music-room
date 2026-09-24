using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicRoom.Application;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Security;
using MusicRoom.Persistence;
using MusicRoom.WebAPI.Endpoints;
using MusicRoom.WebAPI.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace MusicRoom.WebAPI;

/// <summary>
/// The entry point. Top-level statements are not used, because the repository's .editorconfig
/// requires file-scoped namespaces and every type to live in a named namespace.
/// </summary>
public class Program
{
    /// <summary>Never constructed directly; the host owns the lifetime.</summary>
    protected Program()
    {
    }

    /// <summary>Builds the host, wires the middleware, and runs.</summary>
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Serilog first, so a failure during the rest of start-up is still structured — ADR-0022.
        Observability.UseStructuredLogging(builder);

        string connectionString = EnvironmentConfiguration.RequireDatabaseUrl(builder.Configuration);

        builder.Services.AddApplicationServices();
        builder.Services.AddPersistenceServices(connectionString);
        builder.Services.AddInfrastructureServices(
            EnvironmentConfiguration.ReadBCryptWorkFactor(builder.Configuration));

        ApiServices.Add(builder.Services);
        Observability.AddTracing(builder.Services, builder.Configuration);
        HealthRegistration.Add(builder.Services, connectionString);

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseRateLimiter();

        // openapi-documentation: the raw document is served in every environment. An interactive UI
        // is a separate, Development-only concern, and it needs a UI package no ADR has chosen yet.
        app.MapOpenApi();
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();

        await app.RunAsync();
    }
}

/// <summary>
/// Configuration the application refuses to start without.
/// </summary>
internal static class EnvironmentConfiguration
{
    /// <summary>
    /// Reads the vendor-neutral <c>DATABASE_URL</c> — ADR-0026. There is deliberately no fallback
    /// and no default: a missing connection string fails start-up loudly rather than quietly
    /// pointing the application somewhere nobody intended.
    /// </summary>
    internal static string RequireDatabaseUrl(IConfiguration configuration)
    {
        string? connectionString = configuration["DATABASE_URL"];

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                "DATABASE_URL is not set. Copy .env.example to .env and fill it in — see ADR-0026.")
            : connectionString;
    }

    /// <summary>
    /// Reads the BCrypt cost. The floor is enforced in the hasher, not here: configuration is
    /// allowed to raise the cost and never to drop it below ADR-0007's minimum.
    /// </summary>
    internal static int ReadBCryptWorkFactor(IConfiguration configuration) =>
        configuration.GetValue("Security:BCryptWorkFactor", BCryptPasswordHasher.MinimumWorkFactor);
}

/// <summary>
/// The API surface: the OpenAPI document, the error shape, and the rate limiter.
/// </summary>
internal static class ApiServices
{
    /// <summary>
    /// ADR-0021 chose the built-in rate limiting middleware. The policy below is registered but
    /// attached to no endpoint: partitioning by account and by IP belongs to S1, with
    /// authentication-authorization. Health endpoints are never rate limited.
    /// </summary>
    internal static void Add(IServiceCollection services)
    {
        services.AddOpenApi();
        services.AddProblemDetails();
        services.AddRateLimiter(options => options.AddPolicy("fixed", PartitionByRemoteIp));
    }

    private static RateLimitPartition<string> PartitionByRemoteIp(HttpContext httpContext)
    {
        const int permitsPerWindow = 100;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerWindow,
                Window = TimeSpan.FromMinutes(1),
            });
    }
}

/// <summary>
/// Logging and traces — ADR-0022.
/// </summary>
internal static class Observability
{
    /// <summary>Structured JSON to stdout; the container runtime collects it.</summary>
    internal static void UseStructuredLogging(WebApplicationBuilder builder) =>
        builder.Host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter()));

    /// <summary>
    /// The exporter is off unless <c>OTEL_EXPORTER_ENABLED</c> says otherwise, so ordinary
    /// development and the defence demo need no collector running.
    /// </summary>
    internal static void AddTracing(IServiceCollection services, IConfiguration configuration)
    {
        const string serviceName = "musicroom-api";

        bool exporterEnabled = configuration.GetValue("OTEL_EXPORTER_ENABLED", defaultValue: false);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();

                if (exporterEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            });
    }
}

/// <summary>
/// The readiness dependency set. One entry today; S4 adds the music provider.
/// </summary>
internal static class HealthRegistration
{
    /// <summary>Registers the PostgreSQL readiness probe behind <c>DATABASE_URL</c>.</summary>
    internal static void Add(IServiceCollection services, string connectionString) =>
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name: "database",
                factory: _ => new DatabaseReadinessCheck(connectionString),
                failureStatus: HealthStatus.Unhealthy,
                tags: null));
}
