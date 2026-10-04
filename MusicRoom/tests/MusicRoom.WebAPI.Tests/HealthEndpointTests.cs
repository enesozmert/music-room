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
