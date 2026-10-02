using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Control delegation: the gates, the friendship rule, the revoke and the release — SPEC-020, subject V.2.2.
/// </summary>
/// <remarks>
/// Delegating playback to a stranger is the thing this suite exists to prevent, and `404` rather than `403`
/// everywhere else is what stops the endpoints becoming a way to discover somebody's devices.
/// </remarks>
public sealed class DeviceControlEndpointTests : IAsyncLifetime
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

    /// <summary>With no grant, the controller is the owner — decision 2.</summary>
    [Fact]
    public async Task GetControl_WithNoGrant_ReportsTheOwnerAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("control-undelegated@example.com");

        using HttpClient a = owner;
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await DeviceRequests.GetControlAsync(a, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        ControlBody? body = await response.Content.ReadFromJsonAsync<ControlBody>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(ownerId, body.ControllerUserId);
        Assert.False(body.IsDelegated);
    }

    /// <summary>A stranger cannot read who controls somebody else's device.</summary>
    [Fact]
    public async Task GetControl_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("control-private@example.com");
        (HttpClient stranger, _) = await SignInAsync("control-nosy@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await DeviceRequests.GetControlAsync(b, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The delegation subject V.2.2 asks for, to a friend.</summary>
    [Fact]
    public async Task GrantControl_ToAFriend_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("control-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("control-friend@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        await BefriendAsync(ownerId, friendId);

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, deviceId, friendId, await DeviceRequests.ETagAsync(a, deviceId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // The controller can now read the state, which the stranger above could not.
        using HttpResponseMessage read = await DeviceRequests.GetControlAsync(b, deviceId);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        ControlBody? body = await read.Content.ReadFromJsonAsync<ControlBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(friendId, body.ControllerUserId);
        Assert.True(body.IsDelegated);
    }

    /// <summary>A stranger is refused with `422` naming the rule — decision 4.</summary>
    [Fact]
    public async Task GrantControl_ToAStranger_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("control-strict-owner@example.com");
        (HttpClient stranger, Guid strangerId) = await SignInAsync("control-stranger@example.com");

        using HttpClient a = owner;
        stranger.Dispose();

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await DeviceRequests.GrantAsync(
            a, deviceId, strangerId, await DeviceRequests.ETagAsync(a, deviceId));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Only the owner delegates.</summary>
    [Fact]
    public async Task GrantControl_AsANonOwner_Returns404Async()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("control-not-yours-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("control-not-yours-friend@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        await BefriendAsync(ownerId, friendId);

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // The friend holds control, so they can read the state and hold a real token — which still buys them
        // nothing on the write.
        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, deviceId, friendId, await DeviceRequests.ETagAsync(a, deviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        string etag = await DeviceRequests.ETagAsync(b, deviceId);

        // Act — the controller tries to pass control on to themselves again.
        using HttpResponseMessage response = await DeviceRequests.GrantAsync(b, deviceId, friendId, etag);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A stale token changes nothing and answers `412` — decision 5 on one caller.</summary>
    [Fact]
    public async Task GrantControl_WithAStaleIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("control-stale-owner@example.com");
        (HttpClient first, Guid firstId) = await SignInAsync("control-stale-first@example.com");
        (HttpClient second, Guid secondId) = await SignInAsync("control-stale-second@example.com");

        using HttpClient a = owner;
        first.Dispose();
        second.Dispose();

        await BefriendAsync(ownerId, firstId);
        await BefriendAsync(ownerId, secondId);

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        string stale = await DeviceRequests.ETagAsync(a, deviceId);

        using HttpResponseMessage won = await DeviceRequests.GrantAsync(a, deviceId, firstId, stale);
        Assert.Equal(HttpStatusCode.OK, won.StatusCode);

        // Act — the same token again, which the first grant has already invalidated.
        using HttpResponseMessage lost = await DeviceRequests.GrantAsync(a, deviceId, secondId, stale);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, lost.StatusCode);
    }

    /// <summary>No `If-Match` is treated as a mismatch, as on every other guarded write here.</summary>
    [Fact]
    public async Task GrantControl_WithNoIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("control-no-etag-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("control-no-etag-friend@example.com");

        using HttpClient a = owner;
        friend.Dispose();

        await BefriendAsync(ownerId, friendId);

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await a.PutAsJsonAsync(
            $"{DeviceRequests.DevicesPath}/{deviceId}/control",
            new { granteeUserId = friendId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    /// <summary>The owner revokes.</summary>
    [Fact]
    public async Task ReleaseControl_AsTheOwner_ReturnsItToTheOwnerAsync()
    {
        // Arrange
        DelegatedDevice delegated = await DelegatedDeviceAsync("control-revoke");

        using HttpClient a = delegated.Owner;
        delegated.Controller.Dispose();

        Guid ownerId = delegated.OwnerId;
        Guid deviceId = delegated.DeviceId;
        Guid friendId = delegated.ControllerId;

        // Act
        using HttpResponseMessage released = await DeviceRequests.ReleaseAsync(a, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        ControlBody body = await ControlAsync(a, deviceId);
        Assert.Equal(ownerId, body.ControllerUserId);
        Assert.False(body.IsDelegated);
        Assert.NotEqual(friendId, body.ControllerUserId);
    }

    /// <summary>The controller releases — decision 6's other half.</summary>
    [Fact]
    public async Task ReleaseControl_AsTheController_ReturnsItToTheOwnerAsync()
    {
        // Arrange
        DelegatedDevice delegated = await DelegatedDeviceAsync("control-release");

        using HttpClient a = delegated.Owner;
        using HttpClient b = delegated.Controller;

        Guid ownerId = delegated.OwnerId;
        Guid deviceId = delegated.DeviceId;

        // Act
        using HttpResponseMessage released = await DeviceRequests.ReleaseAsync(b, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);

        ControlBody body = await ControlAsync(a, deviceId);
        Assert.Equal(ownerId, body.ControllerUserId);
    }

    /// <summary>And nobody else.</summary>
    [Fact]
    public async Task ReleaseControl_AsAThirdParty_Returns404Async()
    {
        // Arrange
        DelegatedDevice delegated = await DelegatedDeviceAsync("control-third");
        (HttpClient outsider, _) = await SignInAsync("control-outsider@example.com");

        using HttpClient a = delegated.Owner;
        using HttpClient c = outsider;
        delegated.Controller.Dispose();

        Guid deviceId = delegated.DeviceId;
        Guid friendId = delegated.ControllerId;

        // Act
        using HttpResponseMessage response = await DeviceRequests.ReleaseAsync(c, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(friendId, (await ControlAsync(a, deviceId)).ControllerUserId);
    }

    /// <summary>Releasing a device nobody was given is `404`: there was no grant to release.</summary>
    [Fact]
    public async Task ReleaseControl_WithNoGrant_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("control-nothing-to-release@example.com");

        using HttpClient a = owner;
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await DeviceRequests.ReleaseAsync(a, deviceId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The caller sees what was handed to them, and nothing else.</summary>
    [Fact]
    public async Task ListControlGrants_ReturnsWhatTheCallerHoldsAsync()
    {
        // Arrange
        DelegatedDevice delegated = await DelegatedDeviceAsync("control-grants");

        using HttpClient a = delegated.Owner;
        using HttpClient b = delegated.Controller;

        Guid ownerId = delegated.OwnerId;
        Guid deviceId = delegated.DeviceId;

        // Act
        IReadOnlyList<DeviceRequests.GrantBody> theirs = await DeviceRequests.GrantsAsync(b);
        IReadOnlyList<DeviceRequests.GrantBody> mine = await DeviceRequests.GrantsAsync(a);

        // Assert
        DeviceRequests.GrantBody only = Assert.Single(theirs);
        Assert.Equal(deviceId, only.DeviceId);
        Assert.Equal(ownerId, only.OwnerId);
        Assert.False(string.IsNullOrWhiteSpace(only.OwnerDisplayName));

        // The owner holds no grant: they own the device, which is a different thing.
        Assert.Empty(mine);
    }

    /// <summary>An owner, a friend who holds control, and the device between them.</summary>
    /// <remarks>
    /// The friend's client is handed back rather than disposed: <c>TestBearer.SignInAsync</c> registers the
    /// address, and a second registration of an address that already exists answers `202` and sends **no** mail —
    /// correctly, because it must not be an enumeration oracle — so signing the same account in twice through it
    /// finds no token and fails inside the helper.
    /// </remarks>
    private async Task<DelegatedDevice> DelegatedDeviceAsync(string prefix)
    {
        (HttpClient owner, Guid ownerId) = await SignInAsync($"{prefix}-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync($"{prefix}-friend@example.com");

        await BefriendAsync(ownerId, friendId);

        Guid deviceId = await DeviceRequests.RegisterAsync(owner);

        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            owner, deviceId, friendId, await DeviceRequests.ETagAsync(owner, deviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        return new DelegatedDevice(owner, ownerId, friend, friendId, deviceId);
    }

    private static async Task<ControlBody> ControlAsync(HttpClient client, Guid deviceId)
    {
        using HttpResponseMessage response = await DeviceRequests.GetControlAsync(client, deviceId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ControlBody? body = await response.Content.ReadFromJsonAsync<ControlBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body;
    }

    /// <summary>
    /// Makes two accounts friends by writing the row the graph would.
    /// </summary>
    /// <remarks>
    /// Seeded rather than driven through the request and accept endpoints: SPEC-012 proves those, and what this
    /// suite is testing is the delegation rule that reads the result.
    /// </remarks>
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

    private async Task<(HttpClient Client, Guid UserId)> SignInAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Guid userId = await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        return (client, userId);
    }

    /// <summary>The control state, as a client reads it.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="OwnerId">Its owner.</param>
    /// <param name="ControllerUserId">Who may command playback.</param>
    /// <param name="IsDelegated">Whether that is somebody other than the owner.</param>
    private sealed record ControlBody(
        Guid DeviceId,
        Guid OwnerId,
        Guid ControllerUserId,
        bool IsDelegated);

    /// <summary>An owner, the friend holding control of one of their devices, and the device.</summary>
    /// <param name="Owner">The owner's client.</param>
    /// <param name="OwnerId">The owner's account.</param>
    /// <param name="Controller">The controller's client.</param>
    /// <param name="ControllerId">The controller's account.</param>
    /// <param name="DeviceId">The device.</param>
    private sealed record DelegatedDevice(
        HttpClient Owner,
        Guid OwnerId,
        HttpClient Controller,
        Guid ControllerId,
        Guid DeviceId);
}
