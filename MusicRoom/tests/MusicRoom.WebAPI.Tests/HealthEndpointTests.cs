using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// API integration layer — testing standard. Proves routing, status codes and the readiness
/// contract in docs/api/ENDPOINTS.md. No database and no container: the readiness check is
/// substituted, because what is under test here is the endpoint, not PostgreSQL.
/// The Async suffix on each test name is the repository .editorconfig's rule for async methods; the
/// rest of the name is the testing standard's Method_Scenario_ExpectedResult.
/// </summary>
public sealed class HealthEndpointTests
{
    /// <summary>A key-shaped value. Nothing calls YouTube with it: the check reads only whether it exists.</summary>
    internal const string CatalogApiKey = "a-key-that-stands-in-for-the-real-one";

    internal const string TestConnectionString =
        "Host=localhost;Port=5432;Database=musicroom;Username=musicroom;Password=not-a-real-password";

    /// <summary>Liveness answers while the process serves, touching no dependency.</summary>
    [Fact]
    public async Task Get_Health_ReturnsOkAsync()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = CreateFactory(HealthCheckResult.Healthy());
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Readiness returns 200 when every dependency reports reachable.</summary>
    [Fact]
    public async Task GetHealthReady_WhenDatabaseReachable_Returns200Async()
    {
        // Arrange
        using WebApplicationFactory<Program> factory =
            CreateFactory(HealthCheckResult.Healthy("reachable"));
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Readiness fails closed with 503 rather than reporting ready.</summary>
    [Fact]
    public async Task GetHealthReady_WhenDatabaseUnreachable_Returns503Async()
    {
        // Arrange
        using WebApplicationFactory<Program> factory =
            CreateFactory(HealthCheckResult.Unhealthy("not reachable"));
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>The readiness body discloses no host, connection string or exception text.</summary>
    [Fact]
    public async Task GetHealthReady_WhenDatabaseUnreachable_BodyLeaksNothingAsync()
    {
        // Arrange — the failure carries a reason. The response must not.
        using WebApplicationFactory<Program> factory = CreateFactory(HealthCheckResult.Unhealthy(
            "not reachable",
            new InvalidOperationException("db.internal:5432 refused")));
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("db.internal", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestConnectionString, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no catalog key the catalog reports `Degraded` and readiness still answers 200 — SPEC-053
    /// decisions 3 and 4.
    /// </summary>
    /// <remarks>
    /// The real check runs here: only the database registration is substituted, so what is proven is the
    /// registration the application makes, not a stub of it. Readiness must survive, because signing in,
    /// profiles, playlists, events, votes, devices and playback touch no provider.
    /// </remarks>
    [Fact]
    public async Task GetHealthReady_WithNoCatalogKey_ReportsDegradedAndStaysReadyAsync()
    {
        // Arrange — YOUTUBE_API_KEY is deliberately not set.
        using WebApplicationFactory<Program> factory = CreateFactoryWithCatalog(catalogApiKey: null);
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert — reported, and not fatal.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"catalog\":\"Degraded\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// With a key the catalog reports `Healthy`, and the key itself is nowhere in the body — SPEC-053 gate 5.
    /// </summary>
    /// <remarks>
    /// The check never calls YouTube: a `search.list` costs 100 of 10,000 daily quota units, so a probe that
    /// reached the provider would spend the budget it exists to watch. A configured key is the whole claim.
    /// </remarks>
    [Fact]
    public async Task GetHealthReady_WithACatalogKey_ReportsHealthyAndLeaksNoKeyAsync()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = CreateFactoryWithCatalog(CatalogApiKey);
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response =
            await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"catalog\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(CatalogApiKey, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces the database probe and leaves every other registration alone, so the catalog check under test
    /// is the one the application registers.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactoryWithCatalog(string? catalogApiKey)
    {
        IHealthCheck database = Substitute.For<IHealthCheck>();
        database.CheckHealthAsync(Arg.Any<HealthCheckContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Healthy("reachable")));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", TestConnectionString);
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);

            if (catalogApiKey is not null)
            {
                webHost.UseSetting("YOUTUBE_API_KEY", catalogApiKey);
            }

            webHost.ConfigureTestServices(services =>
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    // Only the database entry goes: PostgreSQL is not what this test is about, and the
                    // catalog entry must stay the real one.
                    HealthCheckRegistration? real = options.Registrations
                        .FirstOrDefault(registration => registration.Name == "database");

                    if (real is not null)
                    {
                        options.Registrations.Remove(real);
                    }

                    options.Registrations.Add(new HealthCheckRegistration(
                        name: "database",
                        factory: _ => database,
                        failureStatus: HealthStatus.Unhealthy,
                        tags: null));
                }));
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(HealthCheckResult result)
    {
        IHealthCheck stub = Substitute.For<IHealthCheck>();
        stub.CheckHealthAsync(Arg.Any<HealthCheckContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            // DATABASE_URL is required at start-up and has no fallback, by design — ADR-0026.
            webHost.UseSetting("DATABASE_URL", TestConnectionString);

            // The JWT settings are start-up requirements too, since SPEC-005.
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);

            webHost.ConfigureTestServices(services =>
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                    options.Registrations.Add(new HealthCheckRegistration(
                        name: "database",
                        factory: _ => stub,
                        failureStatus: HealthStatus.Unhealthy,
                        tags: null));
                }));
        });
    }
}
