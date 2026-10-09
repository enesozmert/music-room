using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Validation;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The failure nobody decided — issue O29, SPEC-039.
/// </summary>
/// <remarks>
/// Before this, `AddProblemDetails` was registered and `UseExceptionHandler` was never called, so an
/// unexpected exception answered a bare `500` with no body while a validation failure answered a proper `422`.
/// One API, two envelopes.
/// <para>
/// The exception is provoked by replacing a dependency with one that throws rather than by adding a route that
/// throws: a test-only route proves a test-only pipeline, and what matters here is that a real route's real
/// failure is caught. `IBreachedPasswordSet` is the dependency because it is consulted inside validation, which
/// is as deep into the request as this suite can reach without writing a row.
/// </para>
/// </remarks>
public sealed class UnexpectedExceptionTests : IAsyncLifetime
{
    /// <summary>The text the stub throws with. It must appear in no response.</summary>
    private const string _secretDetail = "connection string Host=secret-host;Password=hunter2";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine and replaces the breached-password set with one that throws.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailService>(_mail);
                services.AddSingleton<IBreachedPasswordSet>(new ThrowingBreachedPasswordSet());
            });
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

    /// <summary>A route that throws answers `500` with the one body every failure shares.</summary>
    [Fact]
    public async Task AnUnexpectedException_AnswersProblemDetailsWithTheCodeAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationEndpointTests.RegistrationsPath,
            new { email = "unexpected@example.com", password = "a passphrase that fits", displayName = "Player" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ValidationCodes.InternalUnexpected, await ValidationProblems.CodeAsync(response));
    }

    /// <summary>And says nothing about what happened — decision 2.</summary>
    /// <remarks>
    /// Asserted by absence, which is the only way to assert a disclosure rule: the stub throws with a string
    /// shaped like a connection string, and no part of it, nor the exception's type name, may appear anywhere
    /// in the body. A handler that wrote `detail` from the exception would pass the test above and fail this
    /// one, which is why the two are separate.
    /// </remarks>
    [Fact]
    public async Task AnUnexpectedException_LeaksNothingAboutItselfAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationEndpointTests.RegistrationsPath,
            new { email = "leaks@example.com", password = "a passphrase that fits", displayName = "Player" },
            TestContext.Current.CancellationToken);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("hunter2", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-host", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        // The literal rather than the constant: `UnexpectedExceptionHandler` is internal to the web project
        // and widening a type's visibility for one assertion is the wrong trade. The title is a published
        // string, so writing it out here is the same kind of contract assertion as the code above.
        Assert.Contains("Unexpected error", body, StringComparison.Ordinal);
    }

    /// <summary>A set that fails the way an unavailable dependency does.</summary>
    private sealed class ThrowingBreachedPasswordSet : IBreachedPasswordSet
    {
        /// <inheritdoc />
        public bool Contains(string password) => throw new InvalidOperationException(_secretDetail);
    }
}
