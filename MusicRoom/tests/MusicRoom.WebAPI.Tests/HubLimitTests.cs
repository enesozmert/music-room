using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Hubs;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The three realtime controls of [[ADR-0030]]: a connection ceiling, a per-connection call limit, and the
/// handshake origin check — SPEC-026, issue O21, threat T-12.
/// </summary>
/// <remarks>
/// Each test builds its own host with the ceiling it is about, the way `CredentialRateLimitingTests` does, so the
/// counts are fresh and the order the suite runs in cannot change a result. The numbers used here are small on
/// purpose: proving the sixth connection is refused does not require opening five.
/// <para>
/// One PostgreSQL container is shared, because these limits are in process and have nothing to do with the
/// database.
/// </para>
/// </remarks>
public sealed class HubLimitTests : IAsyncLifetime
{
    internal const string AllowedOrigin = "https://app.example.com";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    /// <summary>Starts the engine and brings the schema up once.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory = CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    /// <summary>
    /// One account may hold the ceiling and not one more — the per-user half of ADR-0030's ceiling.
    /// </summary>
    [Fact]
    public async Task Connect_BeyondThePerUserCeiling_IsRefusedAsync()
    {
        // Arrange — two allowed, and plenty of room per address so only the user ceiling can refuse.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("HUB_CONNECTIONS_PER_USER", "2"),
            ("HUB_CONNECTIONS_PER_IP", "50"));

        string token = await TokenAsync(factory, "hub-ceiling-user@example.com");

        await using HubConnection first = HubClients.Build(factory, token);
        await using HubConnection second = HubClients.Build(factory, token);
        await using HubConnection third = HubClients.Build(factory, token);

        // Act
        await first.StartAsync(TestContext.Current.CancellationToken);
        await second.StartAsync(TestContext.Current.CancellationToken);

        // Assert — the third is refused, and the two that hold slots are untouched.
        await Assert.ThrowsAnyAsync<Exception>(
            () => third.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HubConnectionState.Connected, first.State);
        Assert.Equal(HubConnectionState.Connected, second.State);
    }

    /// <summary>
    /// Closing a connection frees its slot.
    /// </summary>
    /// <remarks>
    /// The test this control exists or fails by. A ceiling that counts up and never down turns a client that
    /// reconnects into a client that can never connect again, which is worse than the abuse it bounds.
    /// </remarks>
    [Fact]
    public async Task Connect_AfterClosingOne_ReusesTheSlotAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("HUB_CONNECTIONS_PER_USER", "1"),
            ("HUB_CONNECTIONS_PER_IP", "50"));

        string token = await TokenAsync(factory, "hub-ceiling-release@example.com");

        HubConnection first = HubClients.Build(factory, token);
        await first.StartAsync(TestContext.Current.CancellationToken);

        // Act — close it, then connect again.
        await first.StopAsync(TestContext.Current.CancellationToken);
        await first.DisposeAsync();

        await using HubConnection second = HubClients.Build(factory, token);
        await second.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, second.State);
    }

    /// <summary>
    /// The address ceiling holds across accounts — the venue case ADR-0030 names.
    /// </summary>
    [Fact]
    public async Task Connect_BeyondThePerAddressCeiling_IsRefusedAcrossAccountsAsync()
    {
        // Arrange — one connection per address, and a generous per-user ceiling so only the address can refuse.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("HUB_CONNECTIONS_PER_USER", "50"),
            ("HUB_CONNECTIONS_PER_IP", "1"));

        string first = await TokenAsync(factory, "hub-ceiling-ip-a@example.com");
        string second = await TokenAsync(factory, "hub-ceiling-ip-b@example.com");

        await using HubConnection held = HubClients.Build(factory, first);
        await using HubConnection other = HubClients.Build(factory, second);

        // Act
        await held.StartAsync(TestContext.Current.CancellationToken);

        // Assert — a different account from the same address is refused, which is the whole point of the key.
        await Assert.ThrowsAnyAsync<Exception>(
            () => other.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A connection may invoke the allowance and not one more, and the refusal is audited.
    /// </summary>
    [Fact]
    public async Task Invoke_BeyondThePerConnectionLimit_ThrowsAndIsAuditedAsync()
    {
        // Arrange — two invocations a minute.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("HUB_INVOCATIONS_PER_MINUTE", "2"));

        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(
            factory, _mail, "hub-invocations@example.com");

        using HttpClient owner = client;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(owner, "Throttled");

        await using HubConnection connection = HubClients.Build(factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act — joining the same group twice is idempotent, so what differs is only the count.
        await connection.InvokeAsync(PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);
        await connection.InvokeAsync(PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);

        HubException refused = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync(
                PlaylistHub.JoinMethod,
                playlistId,
                TestContext.Current.CancellationToken));

        // Assert — the client is told to slow down, and the audit log says the action was refused.
        Assert.Contains("Too many", refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await RefusedJoinCountAsync(factory));
    }

    /// <summary>A handshake from an allowed origin connects.</summary>
    [Fact]
    public async Task Connect_FromAnAllowedOrigin_SucceedsAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("CORS_ALLOWED_ORIGINS", AllowedOrigin));

        string token = await TokenAsync(factory, "hub-origin-allowed@example.com");

        await using HubConnection connection = HubClients.Build(factory, token, origin: AllowedOrigin);

        // Act
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    /// <summary>An origin that is not on the list is refused at the handshake.</summary>
    [Fact]
    public async Task Connect_FromAnUnlistedOrigin_IsRefusedAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("CORS_ALLOWED_ORIGINS", AllowedOrigin));

        string token = await TokenAsync(factory, "hub-origin-refused@example.com");

        await using HubConnection connection = HubClients.Build(
            factory,
            token,
            origin: "https://not-the-app.example.com");

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A handshake with no `Origin` connects, which is the native client ADR-0030 deliberately admits.
    /// </summary>
    /// <remarks>
    /// The hole is recorded rather than closed: any non-browser client can omit the header, and the control
    /// standing behind it is the JWT, checked on every handshake and re-checked on reconnect.
    /// </remarks>
    [Fact]
    public async Task Connect_WithNoOrigin_SucceedsAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("CORS_ALLOWED_ORIGINS", AllowedOrigin));

        string token = await TokenAsync(factory, "hub-origin-absent@example.com");

        await using HubConnection connection = HubClients.Build(factory, token);

        // Act
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    private async Task<string> TokenAsync(WebApplicationFactory<Program> factory, string email)
    {
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(factory, _mail, email);
        client.Dispose();

        return pair.AccessToken;
    }

    private static async Task<int> RefusedJoinCountAsync(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .CountAsync(
                record => record.Action == "playlist.join" && record.Outcome == "refused",
                TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);

            foreach ((string key, string value) in settings)
            {
                webHost.UseSetting(key, value);
            }

            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
        });
}
