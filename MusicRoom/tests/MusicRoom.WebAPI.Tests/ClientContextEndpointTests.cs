using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Middleware;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The mandatory client headers — subject V.6, SDD §10.7 step 2, SPEC-008.
/// </summary>
/// <remarks>
/// This suite builds its own clients rather than using <c>CreateAuditedClient</c>: the headers are what is
/// under test, so adding them by default would prove nothing.
/// </remarks>
public sealed class ClientContextEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>A mutation without the headers is refused — the rule, enforced rather than documented.</summary>
    [Fact]
    public async Task Mutation_WithoutClientHeaders_Returns400Async()
    {
        // Arrange
        using HttpClient client = _factory.CreateClient();

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, "no-headers@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>All three are required, not any one of them.</summary>
    [Theory]
    [InlineData(ClientContextMiddleware.PlatformHeader)]
    [InlineData(ClientContextMiddleware.DeviceHeader)]
    [InlineData(ClientContextMiddleware.AppVersionHeader)]
    public async Task Mutation_WithOneHeaderMissing_Returns400Async(string omitted)
    {
        // Arrange
        using HttpClient client = _factory.CreateClient();

        foreach (string header in new[]
        {
            ClientContextMiddleware.PlatformHeader,
            ClientContextMiddleware.DeviceHeader,
            ClientContextMiddleware.AppVersionHeader,
        })
        {
            if (!string.Equals(header, omitted, StringComparison.Ordinal))
            {
                client.DefaultRequestHeaders.Add(header, "present");
            }
        }

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, $"missing-{omitted}@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>With all three present the request proceeds as it did before this slice.</summary>
    [Fact]
    public async Task Mutation_WithAllThreeHeaders_ProceedsAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, "with-headers@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>
    /// An overlong value is refused rather than truncated: the column stores 64 characters, and a truncated
    /// audit record is a wrong one.
    /// </summary>
    [Fact]
    public async Task Mutation_WithAnOverlongHeaderValue_Returns400Async()
    {
        // Arrange
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            ClientContextMiddleware.PlatformHeader,
            new string('p', ClientContextMiddleware.MaximumValueLength + 1));
        client.DefaultRequestHeaders.Add(ClientContextMiddleware.DeviceHeader, "device");
        client.DefaultRequestHeaders.Add(ClientContextMiddleware.AppVersionHeader, "1.0.0");

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, "overlong@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A probe is not a client action, so the health endpoints are untouched.</summary>
    [Fact]
    public async Task Health_WithoutClientHeaders_Returns200Async()
    {
        // Arrange
        using HttpClient client = _factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(
            "/health",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The rule is about mutations: a read without the headers is unaffected.</summary>
    [Fact]
    public async Task Get_WithoutClientHeaders_IsUnaffectedAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateClient();

        // Act — the generated document is the only GET that exists outside /health.
        using HttpResponseMessage response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostRegistrationAsync(HttpClient client, string email) =>
        await client.PostAsJsonAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);
}
