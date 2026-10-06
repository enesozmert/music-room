using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The interactive document page, and where it is not served — ADR-0035, SPEC-032.
/// </summary>
/// <remarks>
/// Three assertions carry the decision: the page answers on a Development host, it answers `404` on one that is
/// not, and the raw document answers on **both**. The third is what stops the second passing for the wrong
/// reason — a host that had stopped serving the document would otherwise look like a correctly hidden UI.
/// <para>
/// No database and no mail: these tests fetch two paths and reach no handler, so a connection string that points
/// nowhere is enough.
/// </para>
/// </remarks>
public sealed class SwaggerUiEndpointTests
{
    internal const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=musicroom;Username=musicroom;Password=unused";

    /// <summary>The page a developer opens.</summary>
    [Fact]
    public async Task Ui_OnADevelopmentHost_Returns200Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = Development();
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/swagger/index.html", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — and it is the UI, not the document: the page is HTML.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And nowhere else. An unauthenticated catalogue of every endpoint is an attack-surface map.
    /// </summary>
    [Fact]
    public async Task Ui_OnAProductionHost_Returns404Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = Production();
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/swagger/index.html", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The raw document answers in every environment, which is what ADR-0035 preserved.
    /// </summary>
    /// <remarks>
    /// The load-bearing test of the three. Without it, a host that had stopped publishing the document would
    /// pass the `404` above and look correct.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Document_OnBothHosts_Returns200Async(bool development)
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = development ? Development() : Production();
        using HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — and it is the document the UI points at.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"openapi\"", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> Development() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment("Development");
            Configure(webHost);
            TestMailSettings.Apply(webHost);
        });

    /// <summary>
    /// A host that is not Development, configured so it can actually boot.
    /// </summary>
    /// <remarks>
    /// Deliberately **not** `TestMailSettings`, which sets `MAIL_PROVIDER=logging` — a sender a non-Development
    /// host refuses by design (T-26, and `MailProviderStartupTests.Startup_OnProductionWithTheLoggingProvider_Throws`
    /// proves the refusal). So this one names `brevo` with a placeholder key and sender: placeholders on the same
    /// footing as the connection string above, and no test here issues a request that sends mail, so nothing
    /// reaches a provider or spends its quota (T-19).
    /// </remarks>
    private static WebApplicationFactory<Program> Production() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment("Production");
            Configure(webHost);
            webHost.UseSetting("MAIL_PROVIDER", "brevo");
            webHost.UseSetting("MAIL_API_KEY", "swagger-ui-test-placeholder-not-a-secret");
            webHost.UseSetting("MAIL_FROM", "placeholder@loadtest.invalid");
        });

    private static void Configure(IWebHostBuilder webHost)
    {
        webHost.UseSetting("DATABASE_URL", UnusedConnectionString);
        TestJwtSettings.Apply(webHost);
    }
}
