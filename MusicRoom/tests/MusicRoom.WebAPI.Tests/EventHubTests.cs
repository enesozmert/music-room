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
/// `EventHub`'s front door: the handshake, the `member` gate on a join, and the audit records — SPEC-019.
/// </summary>
/// <remarks>
/// The second hub must be as closed as the first. These tests exist so that "the queue is live" cannot quietly
/// mean "anybody who guesses an event id can watch a private event".
/// </remarks>
public sealed class EventHubTests : IAsyncLifetime
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
            TestMailSettings.Apply(webHost);
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
            _factory, _mail, "event-hub-connect@example.com");
        owner.Dispose();

        await using HubConnection connection = Connect(pair);

        // Act
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    /// <summary>The handshake rule covers the second hub, through the same middleware — ADR-0019.</summary>
    [Fact]
    public async Task Connect_WithoutClientContext_IsRefusedAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-no-context@example.com");
        owner.Dispose();

        await using HubConnection connection = HubClients.Build(
            _factory, pair.AccessToken, withClientContext: false, path: EventHub.Path);

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The hub is not an unauthenticated side door.</summary>
    [Fact]
    public async Task Connect_WithoutAToken_IsRefusedAsync()
    {
        // Arrange
        await using HubConnection connection = HubClients.Build(
            _factory, accessToken: null, path: EventHub.Path);

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Anybody may listen to a public event.</summary>
    [Fact]
    public async Task Join_APublicEvent_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-hub-public@example.com");
        (HttpClient listener, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-listener@example.com");

        using HttpClient a = owner;
        listener.Dispose();

        Guid eventId = await EventRequests.CreateAsync(
            a, "Open event", Visibility.Public, LicenceMode.Everyone);

        await using HubConnection connection = Connect(pair);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act, Assert — the call returning is the assertion: a refusal is a HubException.
        await connection.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken);
    }

    /// <summary>A private event cannot be listened to by a stranger, and the refusal says nothing extra.</summary>
    [Fact]
    public async Task Join_APrivateEventTheCallerMayNotSee_IsRefusedAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-hub-private@example.com");
        (HttpClient stranger, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-stranger@example.com");

        using HttpClient a = owner;
        stranger.Dispose();

        Guid eventId = await EventRequests.CreateAsync(a, "Private", Visibility.Private);

        await using HubConnection connection = Connect(pair);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act
        HubException refusal = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("No such event", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A join is audited, with the client context from the handshake — ADR-0019.</summary>
    [Fact]
    public async Task Join_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-audited-join@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(
            a, "Audited join", Visibility.Public, LicenceMode.Everyone);

        await using HubConnection connection = Connect(pair);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await connection.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken);

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("event.join");

        Assert.NotNull(record.UserId);
        Assert.Equal(eventId, record.TargetId);
        Assert.Equal("event", record.TargetType);
        Assert.Equal(HubClients.Platform, record.Platform);
        Assert.Equal("completed", record.Outcome);
    }

    /// <summary>A refused join is recorded as refused, like a refused REST mutation.</summary>
    [Fact]
    public async Task Join_WhenRefused_WritesARefusedAuditRecordAsync()
    {
        // Arrange
        (HttpClient stranger, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-refused-join@example.com");
        stranger.Dispose();

        await using HubConnection connection = Connect(pair);
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync(
                EventHub.JoinMethod, Guid.NewGuid(), TestContext.Current.CancellationToken));

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("event.join");

        Assert.Equal("refused", record.Outcome);
    }

    /// <summary>Leaving is audited too — the record covers the way out as well as the way in.</summary>
    [Fact]
    public async Task Leave_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "event-hub-audited-leave@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(
            a, "Audited leave", Visibility.Public, LicenceMode.Everyone);

        await using HubConnection connection = Connect(pair);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken);

        // Act
        await connection.InvokeAsync(EventHub.LeaveMethod, eventId, TestContext.Current.CancellationToken);

        // Assert
        Domain.Entities.AuditRecord record = await RecordAsync("event.leave");

        Assert.Equal(eventId, record.TargetId);
        Assert.Equal("event", record.TargetType);
        Assert.Equal("completed", record.Outcome);
    }

    private HubConnection Connect(TokenPair pair) =>
        HubClients.Build(_factory, pair.AccessToken, path: EventHub.Path);

    private async Task<Domain.Entities.AuditRecord> RecordAsync(string action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Action == action, TestContext.Current.CancellationToken);
    }
}
