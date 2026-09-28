using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Persistence and API integration — testing standard. Runs against a real PostgreSQL in a
/// container, because the in-memory provider does not enforce a unique index and the unique index
/// is exactly what is under test here (ADR-0012).
/// </summary>
public sealed class RegistrationEndpointTests : IAsyncLifetime
{
    internal const string RegistrationsPath = "/api/v1/auth/registrations";
    internal const string Password = "correct horse battery";

    // The same image the compose stack runs, so a constraint proven here is proven on the engine
    // the application actually meets.
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine and brings the schema up with the committed migration.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        // This is also the proof that the migration applies to a clean database.
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

    /// <summary>A fresh address is accepted and stored as an unverified account — subject V.1.</summary>
    [Fact]
    public async Task Post_CreatesAnUnverifiedUserAsync()
    {
        // Arrange
        const string email = "fresh@example.com";
        using HttpClient client = _factory.CreateClient();

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        User user = await SingleUserAsync(email);
        Assert.False(user.IsEmailVerified);
        Assert.Equal("Player One", user.DisplayName);
    }

    /// <summary>The password never reaches storage in any readable form — ADR-0007.</summary>
    [Fact]
    public async Task Post_StoresNoPlaintextPasswordAnywhereInTheRowAsync()
    {
        // Arrange
        const string email = "hashed@example.com";
        using HttpClient client = _factory.CreateClient();

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(client, email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        User user = await SingleUserAsync(email);
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
        Assert.StartsWith("$2", user.PasswordHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// A duplicate address is answered exactly like a fresh one, and writes nothing — SDD §10.1
    /// step 5. A 409 here would tell an anonymous caller who holds an account.
    /// </summary>
    [Fact]
    public async Task Post_AnsweringADuplicateAddress_Returns202AndCreatesNoSecondRowAsync()
    {
        // Arrange
        const string email = "duplicate@example.com";
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage first = await PostRegistrationAsync(client, email);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Act
        using HttpResponseMessage second = await PostRegistrationAsync(client, email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, await CountUsersAsync(email));
    }

    /// <summary>
    /// The concurrency case the definition of done requires: the requests run in parallel and the
    /// assertion is on the final database state. <c>ux_users_email</c> arbitrates the race — a
    /// read-then-write check in C# would let several writers through.
    /// </summary>
    [Fact]
    public async Task Post_TwentyConcurrentRegistrationsOfTheSameAddress_CreateExactlyOneRowAsync()
    {
        // Arrange
        const string email = "contended@example.com";
        const int writers = 20;

        List<HttpClient> clients = [.. Enumerable.Range(0, writers).Select(_ => _factory.CreateClient())];

        try
        {
            // Act
            HttpResponseMessage[] responses = await Task.WhenAll(
                clients.Select(client => PostRegistrationAsync(client, email)));

            // Assert
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal(1, await CountUsersAsync(email));

            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
        finally
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>A payload that fails validation is refused with 422, and writes nothing.</summary>
    [Fact]
    public async Task Post_PasswordShorterThan12Characters_Returns422AndCreatesNoRowAsync()
    {
        // Arrange
        const string email = "tooshort@example.com";
        using HttpClient client = _factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationsPath,
            new { email, password = "short", displayName = "Player One" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync(email));
    }

    private static Task<HttpResponseMessage> PostRegistrationAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(
            RegistrationsPath,
            new { email, password = Password, displayName = "Player One" },
            TestContext.Current.CancellationToken);

    private async Task<User> SingleUserAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountUsersAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.Users
            .AsNoTracking()
            .CountAsync(user => user.Email == email, TestContext.Current.CancellationToken);
    }
}
