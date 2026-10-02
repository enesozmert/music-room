using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Advancing and jumping — SPEC-021, ADR-0017, subject V.2.1 and V.2.2.
/// </summary>
/// <remarks>
/// Three claims live here. The server picks the next item by vote count, which is what makes "a highly-voted track
/// plays earlier" true. A **delegate** can advance, which is what makes delegated control mean something. And a
/// retry with a stale `expectedNowPlayingId` does **not** skip a second track — the guarantee ADR-0017 added that
/// field for.
/// </remarks>
public sealed class PlaybackAdvanceEndpointTests : IAsyncLifetime
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

    /// <summary>
    /// The next item is the next most-voted one — **subject V.2.1: a highly-voted track plays earlier**.
    /// </summary>
    [Fact]
    public async Task Advance_AsTheController_PlaysTheNextMostVotedAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-order@example.com");
        (HttpClient voter, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-voter@example.com");

        using HttpClient a = owner;
        using HttpClient b = voter;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid eventId = await EventRequests.CreateAsync(
            a, "Ranked", Visibility.Public, LicenceMode.Everyone);

        Guid winner = await EventQueueRequests.SuggestAsync(a, eventId, "aaaaaaaaaaa");
        Guid second = await EventQueueRequests.SuggestAsync(a, eventId, "bbbbbbbbbbb");
        Guid last = await EventQueueRequests.SuggestAsync(a, eventId, "ccccccccccc");

        // Two votes for the winner, one for the runner-up, none for the third.
        foreach (HttpClient client in new[] { a, b })
        {
            using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(client, eventId, winner);
            Assert.Equal(HttpStatusCode.Created, voted.StatusCode);
        }

        using HttpResponseMessage runnerUp = await EventQueueRequests.VoteAsync(a, eventId, second);
        Assert.Equal(HttpStatusCode.Created, runnerUp.StatusCode);

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Event, eventId);
        Assert.Equal(winner, started.NowPlayingId);

        // Act
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            a,
            started.SessionId,
            started.NowPlayingId,
            await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert — the runner-up, not the unvoted third.
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        PlaybackRequests.SessionBody now = await PlaybackRequests.SessionAsync(a, started.SessionId);
        Assert.Equal(second, now.NowPlayingId);
        Assert.NotEqual(last, now.NowPlayingId);
    }

    /// <summary>A playlist advances by position.</summary>
    [Fact]
    public async Task Advance_OnAPlaylist_PlaysTheNextPositionAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-playlist@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "In order");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);
        Assert.Equal(first, started.NowPlayingId);

        // Act
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            a,
            started.SessionId,
            first,
            await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);
        Assert.Equal(second, (await PlaybackRequests.SessionAsync(a, started.SessionId)).NowPlayingId);
    }

    /// <summary>**Subject V.2.2: a delegate advances playback.**</summary>
    [Fact]
    public async Task Advance_AsTheDelegate_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId, HttpClient friend, Guid friendId) =
            await FriendsAsync("advance-delegate");

        using HttpClient a = owner;
        using HttpClient b = friend;

        (Guid deviceId, _, Guid sessionId, Guid nowPlaying) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(a);

        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, deviceId, friendId, await DeviceRequests.ETagAsync(a, deviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Act — the delegate advances, holding the session's own token.
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            b, sessionId, nowPlaying, await PlaybackRequests.ETagAsync(b, sessionId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);
        Assert.NotEqual(ownerId, friendId);
    }

    /// <summary>Once delegated, the **owner** cannot advance either: the grant is exclusive.</summary>
    [Fact]
    public async Task Advance_AsTheOwnerAfterDelegating_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _, HttpClient friend, Guid friendId) = await FriendsAsync("advance-exclusive");

        using HttpClient a = owner;
        friend.Dispose();

        (Guid deviceId, _, Guid sessionId, Guid nowPlaying) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(a);

        string etag = await PlaybackRequests.ETagAsync(a, sessionId);

        using HttpResponseMessage granted = await DeviceRequests.GrantAsync(
            a, deviceId, friendId, await DeviceRequests.ETagAsync(a, deviceId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.AdvanceAsync(
            a, sessionId, nowPlaying, etag);

        // Assert — the owner's recourse is to revoke, which is one request away.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A retry whose expectation is already stale returns the current state and skips nothing — **decision 4**.
    /// </summary>
    [Fact]
    public async Task Advance_WithAStaleExpectation_ReturnsTheCurrentStateAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-retry@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Three tracks");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");
        Guid third = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        using HttpResponseMessage once = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, first, await PlaybackRequests.ETagAsync(a, started.SessionId));
        Assert.Equal(HttpStatusCode.OK, once.StatusCode);

        // Act — the same advance again, as a flaky network would send it: the expectation still says `first`.
        using HttpResponseMessage retry = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, first, await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert — success, and the session is still on the second track rather than the third.
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(second, (await PlaybackRequests.SessionAsync(a, started.SessionId)).NowPlayingId);
        Assert.NotEqual(third, (await PlaybackRequests.SessionAsync(a, started.SessionId)).NowPlayingId);
    }

    /// <summary>A stale `If-Match` is `412` — decision 4's other half.</summary>
    [Fact]
    public async Task Advance_WithAStaleIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-stale-etag@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Guarded");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");
        await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        string stale = await PlaybackRequests.ETagAsync(a, started.SessionId);

        using HttpResponseMessage won = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, first, stale);
        Assert.Equal(HttpStatusCode.OK, won.StatusCode);

        // Act — a second advance holding the token from before the first one, and expecting the *current* track,
        // so the expectation is fresh and only the version is stale.
        PlaybackRequests.SessionBody now = await PlaybackRequests.SessionAsync(a, started.SessionId);

        using HttpResponseMessage lost = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, now.NowPlayingId, stale);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, lost.StatusCode);
    }

    /// <summary>Nothing left to play ends the session and answers `409` — decision 5.</summary>
    [Fact]
    public async Task Advance_WithTheQueueExhausted_Returns409_AndEndsTheSessionAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-exhausted@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "One track");
        Guid only = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);
        Assert.Equal(only, started.NowPlayingId);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, only, await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        PlaybackRequests.SessionBody ended = await PlaybackRequests.SessionAsync(a, started.SessionId);
        Assert.NotNull(ended.EndedAtUtc);
        Assert.Null(ended.NowPlayingId);
    }

    /// <summary>A jump to an item of the source plays it.</summary>
    [Fact]
    public async Task Jump_ToAnItemInTheSource_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "jump-inside@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Jumpable");

        await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid target = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Act
        using HttpResponseMessage jumped = await PlaybackRequests.JumpAsync(
            a, started.SessionId, target, await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, jumped.StatusCode);
        Assert.Equal(target, (await PlaybackRequests.SessionAsync(a, started.SessionId)).NowPlayingId);
    }

    /// <summary>A jump outside the source is `404`: the session is not a pointer at anything.</summary>
    [Fact]
    public async Task Jump_ToAnItemInAnotherSource_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "jump-outside@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playing = await PlaylistEndpointTests.CreateAsync(a, "Playing");
        await TrackRequests.AddAsync(a, playing, "aaaaaaaaaaa");

        Guid elsewhere = await PlaylistEndpointTests.CreateAsync(a, "Elsewhere");
        Guid foreignTrack = await TrackRequests.AddAsync(a, elsewhere, "bbbbbbbbbbb");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playing);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.JumpAsync(
            a, started.SessionId, foreignTrack, await PlaybackRequests.ETagAsync(a, started.SessionId));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Two accounts that are friends, with clients for both.</summary>
    private async Task<(HttpClient Owner, Guid OwnerId, HttpClient Friend, Guid FriendId)> FriendsAsync(
        string prefix)
    {
        (HttpClient owner, Guid ownerId) = await SignInAsync($"{prefix}-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync($"{prefix}-friend@example.com");

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        (Guid userA, Guid userB) = Domain.Entities.Friendship.Pair(ownerId, friendId);

        context.Friendships.Add(new Domain.Entities.Friendship
        {
            Id = Guid.NewGuid(),
            UserAId = userA,
            UserBId = userB,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (owner, ownerId, friend, friendId);
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
