using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Which sender a host ends up with, read from a real host rather than from the registration in isolation —
/// SPEC-024 decision 2, ADR-0013, T-26.
/// </summary>
/// <remarks>
/// No database: these tests ask what the container resolved and never reach a handler, so a connection string
/// that points nowhere is enough. The point is the composition root, not the endpoints.
/// <para>
/// The implementation types are internal to <c>MusicRoom.Infrastructure</c>, so each assertion is on the
/// resolved type's name. That is the honest assertion available from outside the layer, and it is the same
/// name a reader of the configuration would expect.
/// </para>
/// </remarks>
public sealed class MailProviderStartupTests
{
    // Internal rather than private: the .editorconfig naming rule asks a private field for an underscore
    // prefix, and a const is a field to that rule.
    internal const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=musicroom;Username=musicroom;Password=unused";

    /// <summary>A Development host with the logging sender keeps the sender SPEC-003 shipped.</summary>
    [Fact]
    public async Task Startup_OnDevelopmentWithTheLoggingProvider_ResolvesTheLoggingSenderAsync()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(
            "Development",
            ("MAIL_PROVIDER", "logging"));

        Assert.Equal("LoggingMailService", ResolveMailServiceName(factory));
    }

    /// <summary>
    /// A Production host with Brevo fully configured starts, and that is what closes O17 and T-26.
    /// </summary>
    [Fact]
    public async Task Startup_OnProductionWithBrevo_ResolvesTheHostedSenderAsync()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(
            "Production",
            ("MAIL_PROVIDER", "brevo"),
            ("MAIL_API_KEY", "a-key-that-stands-in-for-the-real-one"),
            ("MAIL_FROM", "sandbox@loadtest.invalid"));

        Assert.Equal("BrevoMailService", ResolveMailServiceName(factory));
    }

    /// <summary>
    /// A Production host with no mail configuration does not start at all.
    /// </summary>
    /// <remarks>
    /// The pre-SPEC-024 guard refused every non-Development host; this one refuses a host with no sender. The
    /// failure mode it exists to prevent is a typo in <c>MAIL_PROVIDER</c> booting a production host that
    /// silently writes verification links and reset tokens into its log.
    /// </remarks>
    [Fact]
    public void Startup_OnProductionWithNoMailProvider_Throws()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Production");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<IMailService>());

        Assert.Contains("MAIL_PROVIDER", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A Development host without the configuration fails the same way. No implicit default.</summary>
    [Fact]
    public void Startup_OnDevelopmentWithNoMailProvider_Throws()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Development");

        Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<IMailService>());
    }

    /// <summary>The logging sender is still refused outside Development — the T-26 guard, by its new rule.</summary>
    [Fact]
    public void Startup_OnProductionWithTheLoggingProvider_Throws()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(
            "Production",
            ("MAIL_PROVIDER", "logging"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<IMailService>());

        Assert.Contains("T-26", exception.Message, StringComparison.Ordinal);
    }

    private static string ResolveMailServiceName(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IMailService>().GetType().Name;

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment(environment);
            webHost.UseSetting("DATABASE_URL", UnusedConnectionString);
            TestJwtSettings.Apply(webHost);

            foreach ((string key, string value) in settings)
            {
                webHost.UseSetting(key, value);
            }
        });
}
