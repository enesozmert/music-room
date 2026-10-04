using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// `ControlHub`: the handshake, and who hears that control moved — SPEC-022, subject V.2.2.
/// </summary>
/// <remarks>
/// The group is a **person**, joined automatically from the validated token, so the suite's job is to show that
/// both sides of a delegation are told and that nobody else is. `AThirdParty_ReceivesNothing` is the one that
/// makes the group key mean something.
/// </remarks>
public sealed class ControlHubTests : IAsyncLifetime
{
    /// <summary>How long a test waits for a message before calling it lost.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;
    private HubConnectedBarrier _barrier = null!;

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

            // `ControlHub` has no method to round-trip through, so the suite fences on the server's own
            // `OnConnectedAsync` instead of on `StartAsync`, which returns before the group is joined.
            _barrier = HubConnectedBarrier.Install(webHost);
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

    /// <summary>Connecting is the whole subscription: there is no join call to make.</summary>
    [Fact]
    public async Task Connect_WithATokenAndClientContext_SucceedsAsync()
    {
        // Arrange
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "control-hub-connect@example.com");
        client.Dispose();

        await using HubConnection connection = Connect(pair);

        // Act
        await connection.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    /// <summary>ADR-0019's handshake rule covers the third hub, through the same middleware.</summary>
    [Fact]
    public async Task Connect_WithoutClientContext_IsRefusedAsync()
    {
        // Arrange
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "control-hub-no-context@example.com");
        client.Dispose();

        await using HubConnection connection = HubClients.Build(
            _factory, pair.AccessToken, withClientContext: false, path: ControlHub.Path);

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The group key comes from a validated token or not at all.</summary>
    [Fact]
    public async Task Connect_WithoutAToken_IsRefusedAsync()
    {
        // Arrange
        await using HubConnection connection = HubClients.Build(
            _factory, accessToken: null, path: ControlHub.Path);

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The account that gained control is told — decision 2.</summary>
    [Fact]
    public async Task Grant_ReachesTheGranteeAsync()
    {
        // Arrange
        Delegation party = await PartyAsync("control-hub-grantee");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();

        await using HubConnection listener = Connect(party.GranteePair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        TaskCompletionSource<GrantedEvent> received = Listen<GrantedEvent>(
            listener, ControlHub.ControlGrantedEvent);

        // Act
        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, party.DeviceId, party.GranteeId, await DeviceRequests.ETagAsync(a, party.DeviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Assert
        GrantedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(party.DeviceId, message.DeviceId);
        Assert.Equal(party.OwnerId, message.OwnerId);
        Assert.Equal(party.GranteeId, message.ControllerUserId);
    }

    /// <summary>And so is the owner, whose other clients were wrong until now.</summary>
    [Fact]
    public async Task Grant_ReachesTheOwnerAsync()
    {
        // Arrange
        Delegation party = await PartyAsync("control-hub-owner");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();

        await using HubConnection listener = Connect(party.OwnerPair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        TaskCompletionSource<GrantedEvent> received = Listen<GrantedEvent>(
            listener, ControlHub.ControlGrantedEvent);

        // Act
        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, party.DeviceId, party.GranteeId, await DeviceRequests.ETagAsync(a, party.DeviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Assert
        GrantedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(party.GranteeId, message.ControllerUserId);
    }

    /// <summary>A grant that replaces a grant tells the controller it displaced — decision 3.</summary>
    [Fact]
    public async Task Grant_ThatDisplacesAController_SendsControlTakenOverToThemAsync()
    {
        // Arrange — two friends of one owner, and the first already holds control.
        Delegation party = await PartyAsync("control-hub-takeover");
        (HttpClient second, Guid secondId, TokenPair secondPair) =
            await SignInAsync("control-hub-takeover-second@example.com");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();
        second.Dispose();

        await BefriendAsync(party.OwnerId, secondId);

        using HttpResponseMessage first = await DeviceRequests.GrantAsync(
            a, party.DeviceId, party.GranteeId, await DeviceRequests.ETagAsync(a, party.DeviceId));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await using HubConnection displaced = Connect(party.GranteePair);
        await _barrier.StartAsync(displaced, TestContext.Current.CancellationToken);

        TaskCompletionSource<TakenOverEvent> received = Listen<TakenOverEvent>(
            displaced, ControlHub.ControlTakenOverEvent);

        // Act — the owner hands it to somebody else.
        using HttpResponseMessage replaced = await DeviceRequests.GrantAsync(
            a, party.DeviceId, secondId, await DeviceRequests.ETagAsync(a, party.DeviceId));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);

        // Assert
        TakenOverEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(party.DeviceId, message.DeviceId);
        Assert.Equal(secondId, message.ControllerUserId);
        Assert.NotEqual(party.GranteeId, secondPair is null ? Guid.Empty : secondId);
    }

    /// <summary>A revoke reaches the controller who lost it.</summary>
    [Fact]
    public async Task Revoke_ReachesThePreviousControllerAsync()
    {
        // Arrange
        Delegation party = await DelegatedAsync("control-hub-revoked");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();

        await using HubConnection listener = Connect(party.GranteePair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        TaskCompletionSource<RevokedEvent> received = Listen<RevokedEvent>(
            listener, ControlHub.ControlRevokedEvent);

        // Act
        using HttpResponseMessage released = await DeviceRequests.ReleaseAsync(a, party.DeviceId);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        // Assert
        RevokedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(party.DeviceId, message.DeviceId);
        Assert.Equal(party.OwnerId, message.OwnerId);
    }

    /// <summary>And the owner, who has it back.</summary>
    [Fact]
    public async Task Revoke_ReachesTheOwnerAsync()
    {
        // Arrange
        Delegation party = await DelegatedAsync("control-hub-revoked-owner");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();

        await using HubConnection listener = Connect(party.OwnerPair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        TaskCompletionSource<RevokedEvent> received = Listen<RevokedEvent>(
            listener, ControlHub.ControlRevokedEvent);

        // Act
        using HttpResponseMessage released = await DeviceRequests.ReleaseAsync(a, party.DeviceId);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        // Assert
        RevokedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(party.OwnerId, message.OwnerId);
    }

    /// <summary>Nobody else hears about somebody's device — the group is the listener.</summary>
    [Fact]
    public async Task AThirdParty_ReceivesNothingAsync()
    {
        // Arrange
        Delegation party = await PartyAsync("control-hub-third");
        (HttpClient outsider, _, TokenPair outsiderPair) =
            await SignInAsync("control-hub-outsider@example.com");

        using HttpClient a = party.Owner;
        party.Grantee.Dispose();
        outsider.Dispose();

        await using HubConnection listener = Connect(outsiderPair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        List<GrantedEvent> received = Collect<GrantedEvent>(listener, ControlHub.ControlGrantedEvent);

        // Act
        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, party.DeviceId, party.GranteeId, await DeviceRequests.ETagAsync(a, party.DeviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    /// <summary>A refused grant is not a control change, so it tells nobody.</summary>
    [Fact]
    public async Task ARefusedGrant_SendsNothingAsync()
    {
        // Arrange — a grantee who is not a friend, so the grant is `422`.
        (HttpClient owner, Guid ownerId, TokenPair ownerPair) =
            await SignInAsync("control-hub-refused-owner@example.com");
        (HttpClient stranger, Guid strangerId, _) =
            await SignInAsync("control-hub-refused-stranger@example.com");

        using HttpClient a = owner;
        stranger.Dispose();

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        await using HubConnection listener = Connect(ownerPair);
        await _barrier.StartAsync(listener, TestContext.Current.CancellationToken);

        List<GrantedEvent> received = Collect<GrantedEvent>(listener, ControlHub.ControlGrantedEvent);

        // Act
        using HttpResponseMessage refused = await DeviceRequests.GrantAsync(
            a, deviceId, strangerId, await DeviceRequests.ETagAsync(a, deviceId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Empty(received);
        Assert.NotEqual(Guid.Empty, ownerId);
    }

    private HubConnection Connect(TokenPair pair) =>
        HubClients.Build(_factory, pair.AccessToken, path: ControlHub.Path);

    /// <summary>An owner, a friend, and a device — with control not yet delegated.</summary>
    private async Task<Delegation> PartyAsync(string prefix)
    {
        (HttpClient owner, Guid ownerId, TokenPair ownerPair) = await SignInAsync($"{prefix}-owner@example.com");
        (HttpClient grantee, Guid granteeId, TokenPair granteePair) =
            await SignInAsync($"{prefix}-friend@example.com");

        await BefriendAsync(ownerId, granteeId);

        Guid deviceId = await DeviceRequests.RegisterAsync(owner);

        return new Delegation(owner, ownerId, ownerPair, grantee, granteeId, granteePair, deviceId);
    }

    /// <summary>The same, with control already in the friend's hands.</summary>
    private async Task<Delegation> DelegatedAsync(string prefix)
    {
        Delegation party = await PartyAsync(prefix);

        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            party.Owner,
            party.DeviceId,
            party.GranteeId,
            await DeviceRequests.ETagAsync(party.Owner, party.DeviceId));

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        return party;
    }

    /// <summary>Makes two accounts friends by writing the row the graph would.</summary>
    private async Task BefriendAsync(Guid first, Guid second)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        (Guid userA, Guid userB) = Domain.Entities.Friendship.Pair(first, second);

        context.Friendships.Add(new Domain.Entities.Friendship
        {
            Id = Guid.NewGuid(),
            UserAId = userA,
            UserBId = userB,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(HttpClient Client, Guid UserId, TokenPair Pair)> SignInAsync(string email)
    {
        (HttpClient client, TokenPair pair) = await TestBearer.SignInAsync(_factory, _mail, email);

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Guid userId = await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        return (client, userId, pair);
    }

    /// <summary>A promise of the first message of one event name, registered before the act.</summary>
    private static TaskCompletionSource<TMessage> Listen<TMessage>(
        HubConnection connection,
        string eventName)
    {
        TaskCompletionSource<TMessage> received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<TMessage>(eventName, message => received.TrySetResult(message));

        return received;
    }

    /// <summary>Counts every message of one event name, from the moment it is registered.</summary>
    private static List<TMessage> Collect<TMessage>(HubConnection connection, string eventName)
    {
        List<TMessage> received = [];

        connection.On<TMessage>(eventName, message => received.Add(message));

        return received;
    }

    /// <summary>An owner, a friend, their clients and tokens, and the device between them.</summary>
    /// <param name="Owner">The owner's client.</param>
    /// <param name="OwnerId">The owner's account.</param>
    /// <param name="OwnerPair">The owner's tokens, for a hub connection.</param>
    /// <param name="Grantee">The friend's client.</param>
    /// <param name="GranteeId">The friend's account.</param>
    /// <param name="GranteePair">The friend's tokens, for a hub connection.</param>
    /// <param name="DeviceId">The device.</param>
    private sealed record Delegation(
        HttpClient Owner,
        Guid OwnerId,
        TokenPair OwnerPair,
        HttpClient Grantee,
        Guid GranteeId,
        TokenPair GranteePair,
        Guid DeviceId);

    /// <summary>`ControlGranted`, as a client reads it.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="OwnerId">Its owner.</param>
    /// <param name="ControllerUserId">Who holds control now.</param>
    private sealed record GrantedEvent(Guid DeviceId, Guid OwnerId, Guid ControllerUserId);

    /// <summary>`ControlRevoked`, as a client reads it.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="OwnerId">Its owner, who controls it again.</param>
    private sealed record RevokedEvent(Guid DeviceId, Guid OwnerId);

    /// <summary>`ControlTakenOver`, as a client reads it.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="ControllerUserId">Who holds it now.</param>
    private sealed record TakenOverEvent(Guid DeviceId, Guid ControllerUserId);
}
