using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Hubs;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The hub's front door: the handshake, the token, the join gate and the audit record — SPEC-016, ADR-0019.
/// </summary>
/// <remarks>
/// These are the tests that make "every action is logged" true on the second transport, and the ones that stop
/// the hub becoming a way around the authorisation the REST routes enforce.
/// </remarks>
public sealed class PlaylistHubTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender, and brings the schema up.</summary>
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

    /// <summary>A handshake with a token and the three client values connects.</summary>
    [Fact]
    public async Task Connect_WithATokenAndClientContext_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-connect@example.com");
        owner.Dispose();

        await using HubConnection connection = HubClients.Build(_factory, pair.AccessToken);

        // Act
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    /// <summary>
    /// A handshake without platform, device and appVersion is refused — ADR-0019, mirroring the REST `400`.
    /// </summary>
    [Fact]
    public async Task Connect_WithoutClientContext_IsRefusedAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-no-context@example.com");
        owner.Dispose();

        await using HubConnection connection = HubClients.Build(
            _factory, pair.AccessToken, withClientContext: false);

        // Act, Assert — an unattributable action must not be possible on either transport.
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The hub is not an unauthenticated side door.</summary>
    [Fact]
    public async Task Connect_WithoutAToken_IsRefusedAsync()
    {
        // Arrange
        await using HubConnection connection = HubClients.Build(_factory, accessToken: null);

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An owner may join the group for their own playlist.</summary>
    [Fact]
    public async Task Join_AsTheOwner_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-join-owner@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Joinable");

        await using HubConnection connection = HubClients.Build(_factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act, Assert — the call returning is the assertion: a refusal is a HubException.
        await connection.InvokeAsync(
            PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A playlist the caller may not see cannot be joined, and the refusal says no more than the REST `404`.
    /// </summary>
    [Fact]
    public async Task Join_APlaylistTheCallerMayNotSee_IsRefusedAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "hub-private@example.com");
        (HttpClient stranger, TokenPair strangerPair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-stranger@example.com");

        using HttpClient a = owner;
        stranger.Dispose();

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Private", Visibility.Private);

        await using HubConnection connection = HubClients.Build(_factory, strangerPair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act
        HubException refusal = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync(
                PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken));

        // Assert — the message names nothing the caller did not already send.
        Assert.Contains("No such playlist", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A join is audited, with the client context from the handshake — ADR-0019.</summary>
    [Fact]
    public async Task Join_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-audited-join@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Audited join");

        await using HubConnection connection = HubClients.Build(_factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await connection.InvokeAsync(
            PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("playlist.join");

        Assert.NotNull(record.UserId);
        Assert.Equal(playlistId, record.TargetId);
        Assert.Equal("playlist", record.TargetType);
        Assert.Equal(HubClients.Platform, record.Platform);
        Assert.Equal(HubClients.Device, record.Device);
        Assert.Equal(HubClients.AppVersion, record.AppVersion);
        Assert.Equal("completed", record.Outcome);
    }

    /// <summary>A refused join is audited as refused, like a refused REST mutation.</summary>
    [Fact]
    public async Task Join_WhenRefused_WritesARefusedAuditRecordAsync()
    {
        // Arrange
        (HttpClient stranger, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-refused-join@example.com");
        stranger.Dispose();

        await using HubConnection connection = HubClients.Build(_factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act — no such playlist at all, which is the same answer as one the caller may not see.
        await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync(
                PlaylistHub.JoinMethod, Guid.NewGuid(), TestContext.Current.CancellationToken));

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("playlist.join");

        Assert.Equal("refused", record.Outcome);
    }

    /// <summary>Leaving is audited too — the record covers the way out as well as the way in.</summary>
    [Fact]
    public async Task Leave_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "hub-audited-leave@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Audited leave");

        await using HubConnection connection = HubClients.Build(_factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync(
            PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);

        // Act
        await connection.InvokeAsync(
            PlaylistHub.LeaveMethod, playlistId, TestContext.Current.CancellationToken);

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("playlist.leave");

        Assert.Equal(playlistId, record.TargetId);
        Assert.Equal("completed", record.Outcome);
    }

    private async Task<Domain.Entities.AuditRecord> RecordAsync(string action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Action == action, TestContext.Current.CancellationToken);
    }
}
