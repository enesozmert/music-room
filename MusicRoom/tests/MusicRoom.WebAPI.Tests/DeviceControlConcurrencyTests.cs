using System.Net;
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
/// Twenty simultaneous claims on one device — AGENTS.md's parallel rule, on the write subject V.2.2 grades.
/// </summary>
/// <remarks>
/// The contract's own words: two simultaneous claims mean one winner and one `412`, and **the loser must lose
/// visibly**. A silent overwrite would leave two clients each believing they hold the device, which is the
/// failure this endpoint exists to prevent — so the assertion is on the final database state as well as the
/// status codes.
/// </remarks>
public sealed class DeviceControlConcurrencyTests : IAsyncLifetime
{
    /// <summary>How many friends race for one device.</summary>
    internal const int Racers = 20;

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

    /// <summary>
    /// Twenty claims with the same token: one `200`, nineteen `412`, and one controller in the database —
    /// and that controller is one of the twenty, not a value nobody asked for.
    /// </summary>
    [Fact]
    public async Task GrantControl_TwentySimultaneousClaims_ProduceOneWinnerAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("claim-owner@example.com");

        using HttpClient a = owner;
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        List<Guid> friends = [];

        for (int index = 0; index < Racers; index++)
        {
            (HttpClient friend, Guid friendId) = await SignInAsync($"claim-friend-{index}@example.com");
            friend.Dispose();

            await BefriendAsync(ownerId, friendId);
            friends.Add(friendId);
        }

        // Every racer holds the same token, read before any of them wrote.
        string shared = await DeviceRequests.ETagAsync(a, deviceId);

        // Act — the owner delegates to twenty different friends at once, which is the shape of two clients of
        // one account racing: the server must apply exactly one.
        HttpStatusCode[] answers = await Task.WhenAll(
            friends.Select(async friendId =>
            {
                using HttpResponseMessage response = await DeviceRequests.GrantAsync(
                    a, deviceId, friendId, shared);

                return response.StatusCode;
            }));

        // Assert
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(
            Racers - 1,
            answers.Count(status => status == HttpStatusCode.PreconditionFailed));

        Guid? controller = await ControllerAsync(deviceId);

        Assert.NotNull(controller);
        Assert.Contains(controller.Value, friends);
    }

    /// <summary>
    /// Twenty parallel releases leave the device with its owner, and exactly one of them is told it did it.
    /// </summary>
    [Fact]
    public async Task ReleaseControl_TwentyParallelReleases_LeaveItWithTheOwnerAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("release-race-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("release-race-friend@example.com");

        using HttpClient a = owner;
        friend.Dispose();

        await BefriendAsync(ownerId, friendId);

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, deviceId, friendId, await DeviceRequests.ETagAsync(a, deviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Act — the owner revokes twenty times at once, which a retrying client does by accident.
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async _ =>
            {
                using HttpResponseMessage response = await DeviceRequests.ReleaseAsync(a, deviceId);

                return response.StatusCode;
            }));

        // Assert — one release did it, the rest found nothing to release, and the device is the owner's.
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.NoContent));
        Assert.Equal(Racers - 1, answers.Count(status => status == HttpStatusCode.NotFound));
        Assert.Null(await ControllerAsync(deviceId));
    }

    private async Task<Guid?> ControllerAsync(Guid deviceId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceId)
            .Select(device => device.ControllerUserId)
            .SingleAsync(TestContext.Current.CancellationToken);
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
}
