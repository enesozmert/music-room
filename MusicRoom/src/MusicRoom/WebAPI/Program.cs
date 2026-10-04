using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MusicRoom.Application;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Security;
using MusicRoom.Persistence;
using MusicRoom.WebAPI.Endpoints;
using MusicRoom.WebAPI.Hubs;
using MusicRoom.WebAPI.HealthChecks;
using MusicRoom.WebAPI.Middleware;
using MusicRoom.WebAPI.RateLimiting;
using MusicRoom.WebAPI.Realtime;
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

        // Through a helper rather than inline: CA1506 caps `Main` at 41 coupled types and it is at the limit,
        // so SPEC-025's CatalogOptions pushed it over — the same reason CallerServices and RealtimeServices
        // below are their own classes.
        InfrastructureSetup.Add(builder);

        ApiServices.Add(builder.Services, builder.Configuration);

        // The audit behaviour reads the client context through this abstraction; the implementation is a
        // WebAPI type because HttpContext is — SPEC-008.
        CallerServices.Add(builder.Services, builder.Configuration);
        RealtimeServices.Add(builder);
        Observability.AddTracing(builder.Services, builder.Configuration);
        HealthRegistration.Add(builder.Services, connectionString);

        WebApplication app = builder.Build();

        app.UseSerilogRequestLogging();

        // Before authentication, because a browser's preflight carries no credentials and must be answered
        // whatever the request that follows it needs. The policy is the one `CORS_ALLOWED_ORIGINS` names, and an
        // empty list allows nothing — SPEC-026 decision 6, which is also what gives ADR-0030's handshake check a
        // list to compare against.
        ApiServices.UseCrossOrigin(app);

        // Order is load-bearing, and this is the order ADR-0040 settled after SPEC-023 measured the vote
        // route. Authentication comes first, because the per-account policies partition on the validated
        // `sub` and a limiter that runs before it sees no claims and silently falls back to the address —
        // which turned a per-account vote limit into one bucket for a whole venue. The limiter comes next,
        // before authorisation and before any handler, so a flood is still refused before the request
        // reaches the database. The client-context middleware is last, so a request the limiter refuses is
        // not answered 400 for a missing header — SPEC-008.
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseMiddleware<ClientContextMiddleware>();

        // openapi-documentation: the raw document is served in every environment. An interactive UI
        // is a separate, Development-only concern, and it needs a UI package no ADR has chosen yet.
        app.MapOpenApi();
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapProfileEndpoints();
        app.MapFriendEndpoints();
        app.MapUserEndpoints();
        app.MapPlaylistEndpoints();
        app.MapEventEndpoints();
        app.MapDeviceEndpoints();
        app.MapTrackEndpoints();

        // The hub is mapped after the endpoints and behind the same authentication. Its own authorisation is
        // the [Authorize] on the hub plus the join gate inside it — SPEC-016.
        RealtimeServices.Map(app);

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

    /// <summary>
<<<<<<< HEAD
    /// Reads the realtime ceilings and the allowed origins — [[ADR-0030]], numbers of 2026-10-04.
    /// </summary>
    /// <remarks>
    /// The decided values are the defaults, so a host that overrides nothing still limits at the approved
    /// numbers. A non-positive override is ignored, exactly as the credential limits ignore one: configuration
    /// may raise or lower a ceiling and must not be able to switch it off.
    /// <para>
    /// `CORS_ALLOWED_ORIGINS` is a comma-separated list and an empty value means **no** cross-origin request is
    /// allowed. "Unset" reading as "everything" is how a development convenience becomes a production hole.
    /// </para>
    /// </remarks>
    internal static HubConnectionLimits ReadHubLimits(IConfiguration configuration) =>
        new(
            ReadCeiling(
                configuration,
                "HUB_CONNECTIONS_PER_USER",
                HubConnectionLimits.DefaultConnectionsPerUser),
            ReadCeiling(
                configuration,
                "HUB_CONNECTIONS_PER_IP",
                HubConnectionLimits.DefaultConnectionsPerAddress),
            ReadCeiling(
                configuration,
                "HUB_INVOCATIONS_PER_MINUTE",
                HubConnectionLimits.DefaultInvocationsPerConnectionPerMinute),
            ReadOrigins(configuration));

    /// <summary>The origins a browser may connect from, as `CORS_ALLOWED_ORIGINS` lists them.</summary>
    internal static IReadOnlyList<string> ReadOrigins(IConfiguration configuration) =>
        [
            .. (configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

    private static int ReadCeiling(IConfiguration configuration, string key, int approved)
    {
        int configured = configuration.GetValue(key, approved);

        return configured > 0 ? configured : approved;
    }
=======
    /// Reads the catalog's key and cache lifetimes — ADR-0003, ADR-0023.
    /// </summary>
    /// <remarks>
    /// A missing <c>YOUTUBE_API_KEY</c> is tolerated rather than fatal, and that is deliberate: the catalog then
    /// answers from its two cache levels and reports the provider unavailable. `youtube-integration` requires a
    /// provider outage to degrade rather than stop an event, and a host with no key is the same situation.
    /// <para>
    /// The lifetimes default to ADR-0023's policy, so a host that overrides nothing still caches the way the
    /// decision says.
    /// </para>
    /// </remarks>
    internal static CatalogOptions ReadCatalogOptions(IConfiguration configuration) =>
        new(
            configuration["YOUTUBE_API_KEY"],
            ReadSpan(configuration, "CATALOG_TRACK_LIFETIME_HOURS", TrackRef.DefaultLifetime),
            ReadSpan(configuration, "CATALOG_SEARCH_LIFETIME_HOURS", TrackSearchCache.DefaultLifetime),
            CatalogOptions.DefaultMemoryLifetime,
            CatalogOptions.DefaultRequestTimeout);

    /// <summary>
    /// Reads a lifetime in whole hours, falling back to the decided value.
    /// </summary>
    /// <remarks>
    /// A non-positive override is ignored rather than honoured, for the reason the rate limits are read the same
    /// way: configuration must not be able to switch a cache off and send every read to the provider.
    /// </remarks>
    private static TimeSpan ReadSpan(IConfiguration configuration, string key, TimeSpan approved)
    {
        int hours = configuration.GetValue(key, 0);

        return hours > 0 ? TimeSpan.FromHours(hours) : approved;
    }
}

/// <summary>
/// Builds the infrastructure options from configuration and registers the layer.
/// </summary>
/// <remarks>
/// Its own class rather than more lines in <c>Main</c>, which CA1506 caps at 41 coupled types and which
/// SPEC-025 pushed to 42 by adding <see cref="CatalogOptions"/> to the record — the same reason
/// <c>CallerServices</c> exists.
/// </remarks>
internal static class InfrastructureSetup
{
    /// <summary>Registers the hasher, the token issuer, the attempt limiter, the catalog and the sender.</summary>
    /// <param name="builder">The host being built.</param>
    internal static void Add(WebApplicationBuilder builder) =>
        builder.Services.AddInfrastructureServices(new InfrastructureOptions(
            EnvironmentConfiguration.ReadBCryptWorkFactor(builder.Configuration),
            builder.Environment.IsDevelopment(),
            EnvironmentConfiguration.ReadVerificationLinkBaseUrl(builder.Configuration),
            EnvironmentConfiguration.ReadCatalogOptions(builder.Configuration),
            EnvironmentConfiguration.RequireJwtOptions(builder.Configuration),
            EnvironmentConfiguration.ReadCredentialAttemptLimits(builder.Configuration)));
>>>>>>> origin/main
}

/// <summary>
/// Who is calling: the bearer handler, and the two accessors that read the validated principal.
/// </summary>
/// <remarks>
/// Its own class rather than more lines in <c>Main</c>, which CA1506 caps at 41 coupled types and which this
/// wiring pushed to 42 — SPEC-010.
/// </remarks>
internal static class CallerServices
{
    /// <summary>
    /// Registers bearer validation of the token SPEC-005 issues, and the accessors the pipeline reads it
    /// through. The validation parameters are built in <c>Infrastructure</c>, so the signing key's shape stays
    /// in the project that signs with it.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the JWT settings come from.</param>
    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IClientContextAccessor, HttpClientContextAccessor>();
        services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

        JwtOptions jwtOptions = EnvironmentConfiguration.RequireJwtOptions(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = JwtBearerConfiguration.Build(jwtOptions);

                // No metadata endpoint to reach and no HTTPS requirement on the handler itself: the token is
                // symmetric and TLS is the deployment's job, not this handler's.
                options.RequireHttpsMetadata = false;

                // A WebSocket handshake cannot carry an Authorization header, so SignalR documents the
                // `access_token` query parameter. It is read **only** under /hubs: giving every REST route
                // a second way to present a token would widen the attack surface for one transport's benefit,
                // and a token in a URL is a token that can end up in somebody's history. Serilog's request
                // logging records the path and not the query string, so it does not reach the log either —
                // SPEC-016 decision 4.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = messageReceived =>
                    {
                        if (messageReceived.Request.Path.StartsWithSegments(
                            ClientContextMiddleware.HubPaths))
                        {
                            string? token = messageReceived.Request.Query["access_token"];

                            if (!string.IsNullOrWhiteSpace(token))
                            {
                                messageReceived.Token = token;
                            }
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
    }
}

/// <summary>
/// SignalR, the hub filter, and the broadcaster the application layer sends through — SPEC-016, SPEC-019.
/// </summary>
/// <remarks>
/// Its own class rather than more lines in <c>Main</c>, which CA1506 caps at 41 coupled types — the same
/// reason <c>CallerServices</c> exists.
/// </remarks>
/// <summary>
/// The cross-origin policy, from the list ADR-0030 and the hub handshake share.
/// </summary>
/// <remarks>
/// Added by SPEC-026, which found `CORS_ALLOWED_ORIGINS` in `.env.example` and in no code: the ADR says the
/// handshake validates `Origin` against *"the same list REST uses"*, and REST used none. One list, because two
/// would drift — the reasoning ADR-0030 gives for not adding a second allowlist for the hub.
/// <para>
/// An empty list allows no cross-origin request at all. Credentials are allowed only for the named origins,
/// which is why a wildcard is not an option here even in Development: `AllowAnyOrigin` with
/// `AllowCredentials` is rejected by the framework, and rightly.
/// </para>
/// </remarks>
internal static class RealtimeServices
{
    /// <summary>
    /// How long a connection may be silent before each side acts. Written out because
    /// `signalr-realtime` forbids relying on defaults matching what a mobile client expects: the server
    /// pings at 15 seconds and gives a client 60 to be heard from, which tolerates a train tunnel without
    /// holding a dead connection open for minutes.
    /// </summary>
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    /// <summary>How long the server waits for a client before dropping it.</summary>
    internal static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Maps both hubs.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <remarks>
    /// Here rather than in <c>Main</c>, which CA1506 caps at 41 coupled types and which these generic
    /// arguments pushed past it.
    /// </remarks>
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<PlaylistHub>(PlaylistHub.Path);
        endpoints.MapHub<EventHub>(EventHub.Path);
        endpoints.MapHub<ControlHub>(ControlHub.Path);

        // Mapped here rather than in `Main` for the same reason the hubs are: CA1506 caps that method at 41
        // coupled types, and it is already at the limit.
        endpoints.MapPlaybackEndpoints();
    }

    /// <summary>Registers the hub, its filter, the per-invocation caller and the broadcaster.</summary>
    /// <param name="builder">The host being built. The ceilings are read from its configuration here rather
    /// than in <c>Main</c>, which CA1506 caps at 41 coupled types and which this unit pushed to 42.</param>
    internal static void Add(WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;
        HubConnectionLimits limits = EnvironmentConfiguration.ReadHubLimits(builder.Configuration);

        // Scoped: SignalR creates a scope per invocation, so one instance holds one call's caller. The two
        // accessors read it when there is no HttpContext.
        services.AddScoped<HubCaller>();

        services.AddSignalR(options =>
        {
            options.KeepAliveInterval = KeepAlive;
            options.ClientTimeoutInterval = ClientTimeout;

            // Centrally registered, exactly as the audit behaviour is, and one filter for both hubs: a new hub
            // method is audited because the filter is on the hub, not because somebody remembered — ADR-0019.
            options.AddFilter<HubAuditFilter>();
        });

        services.AddSingleton<HubAuditFilter>();
        services.AddSingleton(limits);
        services.AddSingleton<HubConnectionRegistry>();
        services.AddSingleton(_ => new HubInvocationLimiter(TimeProvider.System));
        services.AddScoped<IRealtimeBroadcaster, RealtimeBroadcaster>();
    }
}

/// <summary>
/// The API surface: the OpenAPI document, the error shape, and the rate limiter.
/// </summary>
internal static class ApiServices
{
    /// <summary>
    /// The name of the one cross-origin policy this API has.
    /// </summary>
    /// <remarks>
    /// One policy, because ADR-0030 refused a second allowlist for the hub on the grounds that two lists drift.
    /// </remarks>
    internal const string CorsPolicy = "musicroom-clients";

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
        EventRateLimiterPolicies.Add(services);
<<<<<<< HEAD
        AddCors(services, configuration);
    }

    /// <summary>
    /// Applies the cross-origin policy to the pipeline.
    /// </summary>
    /// <param name="app">The application being built.</param>
    /// <remarks>
    /// Called from here rather than written inline in <c>Main</c>, which CA1506 caps at 41 coupled types: naming
    /// the CORS extension there was the forty-second.
    /// </remarks>
    internal static void UseCrossOrigin(WebApplication app) => app.UseCors(CorsPolicy);

    /// <summary>Registers the policy named by <see cref="CorsPolicy"/>.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the origin list lives.</param>
    private static void AddCors(IServiceCollection services, IConfiguration configuration)
    {
        string[] origins = [.. EnvironmentConfiguration.ReadOrigins(configuration)];

        services.AddCors(options => options.AddPolicy(
            CorsPolicy,
            policy =>
            {
                if (origins.Length == 0)
                {
                    // No origin is allowed. A browser gets no `Access-Control-Allow-Origin` header and refuses
                    // the request itself, which is the correct answer to an unconfigured list.
                    return;
                }

                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()

                    // The bearer token travels in a header rather than a cookie, so this is not what carries
                    // the credential — it is what lets a browser read the response at all.
                    .AllowCredentials()

                    // `ETag` and `Retry-After` are part of this API's contract, and a browser cannot read either
                    // unless they are exposed: the concurrency guard and the limiter would both be invisible.
                    .WithExposedHeaders("ETag", "Retry-After");
            }));
=======
        CatalogRateLimiterPolicies.Add(services);
>>>>>>> origin/main
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
