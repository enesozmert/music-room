using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Configuration;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The start-up guards, through the abstraction every configuration read now travels — SPEC-030.
/// </summary>
/// <remarks>
/// The point of this suite is that **nothing changed**. Routing thirteen reads through `ISecretProvider` must
/// leave a host that cannot reach a database, cannot sign a token, or has no mail sender refusing to start, with
/// the same messages it refused with before.
/// <para>
/// No database: these tests ask what the host did at start-up and never reach a handler, so a connection string
/// that points nowhere is enough.
/// </para>
/// </remarks>
public sealed class SecretProviderStartupTests
{
    internal const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=musicroom;Username=musicroom;Password=unused";

    /// <summary>A host with no connection string does not start, and the message names the variable.</summary>
    [Fact]
    public void Startup_WithoutADatabaseUrl_ThrowsNamingTheVariable()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                TestJwtSettings.Apply(webHost);
                TestMailSettings.Apply(webHost);
                webHost.UseSetting("DATABASE_URL", string.Empty);
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<ISecretProvider>());

        Assert.Contains("DATABASE_URL", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A host that cannot sign a token does not start.</summary>
    /// <remarks>
    /// The JWT settings have been start-up requirements since SPEC-005; the signing key's own floor is enforced
    /// by `JwtOptions`, and what this asserts is that the requirement survived the refactor.
    /// </remarks>
    [Fact]
    public void Startup_WithoutTheJwtSettings_Throws()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                TestMailSettings.Apply(webHost);
                webHost.UseSetting("DATABASE_URL", UnusedConnectionString);
                webHost.UseSetting("JWT_ISSUER", string.Empty);
                webHost.UseSetting("JWT_AUDIENCE", string.Empty);
                webHost.UseSetting("JWT_ACCESS_TOKEN_MINUTES", string.Empty);
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<ISecretProvider>());

        Assert.Contains("JWT_", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A host with no mail sender does not start — the SPEC-024 guard, unchanged.</summary>
    [Fact]
    public void Startup_WithoutAMailProvider_Throws()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                TestJwtSettings.Apply(webHost);
                webHost.UseSetting("DATABASE_URL", UnusedConnectionString);
                webHost.UseSetting("MAIL_PROVIDER", string.Empty);
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<ISecretProvider>());

        Assert.Contains("MAIL_PROVIDER", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fully configured host starts, and the abstraction is what it resolves.
    /// </summary>
    /// <remarks>
    /// Also the proof that the registration and the start-up construction agree: the container hands out an
    /// `ISecretProvider` that reads what the host was configured with.
    /// </remarks>
    [Fact]
    public void Startup_WithEverythingConfigured_ResolvesTheProvider()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                TestJwtSettings.Apply(webHost);
                TestMailSettings.Apply(webHost);
                webHost.UseSetting("DATABASE_URL", UnusedConnectionString);
            });

        ISecretProvider secrets = factory.Services.GetRequiredService<ISecretProvider>();

        Assert.Equal(UnusedConnectionString, secrets.Require("DATABASE_URL"));
        Assert.Null(secrets.Read("A_VARIABLE_NOBODY_SET"));
    }
}
