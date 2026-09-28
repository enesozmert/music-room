using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicRoom.Application;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Security;
using MusicRoom.Persistence;
using MusicRoom.WebAPI.Endpoints;
using MusicRoom.WebAPI.HealthChecks;
using MusicRoom.WebAPI.Middleware;
using MusicRoom.WebAPI.RateLimiting;
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
        builder.Services.AddInfrastructureServices(new InfrastructureOptions(
            EnvironmentConfiguration.ReadBCryptWorkFactor(builder.Configuration),
            builder.Environment.IsDevelopment(),
            EnvironmentConfiguration.ReadVerificationLinkBaseUrl(builder.Configuration),
            EnvironmentConfiguration.RequireJwtOptions(builder.Configuration),
            EnvironmentConfiguration.ReadCredentialAttemptLimits(builder.Configuration)));

        ApiServices.Add(builder.Services, builder.Configuration);

        // The audit behaviour reads the client context through this abstraction; the implementation is a
        // WebAPI type because HttpContext is — SPEC-008.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IClientContextAccessor, HttpClientContextAccessor>();
        Observability.AddTracing(builder.Services, builder.Configuration);
        HealthRegistration.Add(builder.Services, connectionString);

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();
        app.UseRateLimiter();

        // After the limiter on purpose: a request the limiter refuses must not be answered 400 for a
        // missing header instead of 429 — SPEC-008.
        app.UseMiddleware<ClientContextMiddleware>();

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

    /// <summary>
    /// Reads the four JWT settings — SDD §13.1. Read the way <c>DATABASE_URL</c> is: straight from
    /// configuration, with no default and no fallback, so a host that starts can actually sign a
    /// token. <see cref="JwtOptions"/> enforces the 32-byte floor on the key, and neither the key nor
    /// any part of it is logged or named in a failure message.
    /// </summary>
    internal static JwtOptions RequireJwtOptions(IConfiguration configuration)
    {
        string? signingKey = configuration["JWT_SIGNING_KEY"];
        string? issuer = configuration["JWT_ISSUER"];
        string? audience = configuration["JWT_AUDIENCE"];
        string? accessTokenMinutes = configuration["JWT_ACCESS_TOKEN_MINUTES"];

        if (string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(audience)
            || !int.TryParse(accessTokenMinutes, CultureInfo.InvariantCulture, out int minutes))
        {
            throw new InvalidOperationException(
                "JWT_ISSUER, JWT_AUDIENCE and JWT_ACCESS_TOKEN_MINUTES must all be set, and the last "
                + "must be a whole number of minutes. Copy .env.example to .env and fill it in — see "
                + "ADR-0026.");
        }

        return new JwtOptions(signingKey ?? string.Empty, issuer, audience, minutes);
    }

    /// <summary>
    /// Reads the per-account credential limits — SPEC-006 escalation 1. The approved numbers are the
    /// defaults, so a host with no override still limits at what was signed off; configuration may
    /// change them, which is what lets a test host raise a limit for a suite proving something else.
    /// A non-positive override is ignored rather than trusted: the one outcome configuration must not be
    /// able to produce is no limit at all.
    /// </summary>
    internal static CredentialAttemptLimits ReadCredentialAttemptLimits(IConfiguration configuration)
    {
        int permits = configuration.GetValue(
            "RATE_LIMIT_ACCOUNT_PERMITS",
            CredentialAttemptLimits.DefaultPermitsPerWindow);
        int windowMinutes = configuration.GetValue(
            "RATE_LIMIT_ACCOUNT_WINDOW_MINUTES",
            CredentialAttemptLimits.DefaultWindowMinutes);
        int penaltyWindows = configuration.GetValue(
            "RATE_LIMIT_ACCOUNT_MAX_PENALTY_WINDOWS",
            CredentialAttemptLimits.DefaultMaximumPenaltyWindows);

        return new CredentialAttemptLimits(
            permits > 0 ? permits : CredentialAttemptLimits.DefaultPermitsPerWindow,
            TimeSpan.FromMinutes(
                windowMinutes > 0 ? windowMinutes : CredentialAttemptLimits.DefaultWindowMinutes),
            penaltyWindows > 0 ? penaltyWindows : CredentialAttemptLimits.DefaultMaximumPenaltyWindows);
    }

    /// <summary>
    /// Reads where the client serves the verification screen. Read the way <c>DATABASE_URL</c> is —
    /// straight from configuration, with no default — but unset is tolerated rather than fatal: the
    /// Development sender then logs the bare token and says the variable is missing. A hard-coded
    /// fallback would print a link to a machine nobody intended, which is the failure mode this
    /// avoids. Absent while the hosted provider is still O17 / slice S1a.
    /// </summary>
    internal static string? ReadVerificationLinkBaseUrl(IConfiguration configuration) =>
        configuration["VERIFICATION_LINK_BASE_URL"];
}

/// <summary>
/// The API surface: the OpenAPI document, the error shape, and the rate limiter.
/// </summary>
internal static class ApiServices
{
    /// <summary>
    /// ADR-0021 chose the built-in rate limiting middleware. SPEC-006 replaced the S0 placeholder
    /// policy, which was registered and attached to nothing, with one policy per credential endpoint —
    /// each attached to its route in <c>AuthEndpoints</c>. Health endpoints are never rate limited.
    /// </summary>
    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi();
        services.AddProblemDetails();
        CredentialRateLimiterPolicies.Add(services, configuration);
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
