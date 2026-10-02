using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// Twenty clients advancing one session at once — ADR-0017's fourth hotspot, and the `NowPlayingChanged` it emits.
/// </summary>
/// <remarks>
/// The claim under test is that a double skip is impossible. Twenty advances carrying the same token and the same
/// expectation move the session **once**: the guarded `UPDATE` admits one, and the rest are told either `412`
/// (somebody else moved first) or `200` with the current state (their expectation was already stale). Both are
/// correct answers, and neither of them is a second track.
/// </remarks>
public sealed class PlaybackConcurrencyTests : IAsyncLifetime
{
    /// <summary>How many clients race.</summary>
    internal const int Racers = 20;

    /// <summary>How long a broadcast test waits before calling a message lost.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

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
    /// Twenty parallel advances move the session exactly once — no client, retrying or racing, skips a track.
    /// </summary>
    [Fact]
    public async Task Advance_TwentyParallelAdvances_MoveOnceAsync()
    {
        // Arrange — a playlist of five tracks, so a double skip would be visible as the third or later.
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-race@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Five tracks");

        List<Guid> tracks =
        [
            await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa"),
            await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb"),
            await TrackRequests.AddAsync(a, playlistId, "ccccccccccc"),
            await TrackRequests.AddAsync(a, playlistId, "ddddddddddd"),
            await TrackRequests.AddAsync(a, playlistId, "eeeeeeeeeee"),
        ];

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);
        Assert.Equal(tracks[0], started.NowPlayingId);

        string shared = await PlaybackRequests.ETagAsync(a, started.SessionId);

        // Act — every racer holds the same token and expects the same track, which is one controller retrying and
        // two controllers racing at the same time.
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async _ =>
            {
                using HttpResponseMessage response = await PlaybackRequests.AdvanceAsync(
                    a, started.SessionId, tracks[0], shared);

                return response.StatusCode;
            }));

        // Assert — the session moved to the second track and no further.
        PlaybackRequests.SessionBody now = await PlaybackRequests.SessionAsync(a, started.SessionId);

        Assert.Equal(tracks[1], now.NowPlayingId);
        Assert.Null(now.EndedAtUtc);

        // One request did the move. Every other answer is a refusal (412) or a correct no-op (200), and never a second move.
        Assert.Contains(HttpStatusCode.OK, answers);
        Assert.All(answers, status => Assert.True(status is HttpStatusCode.OK or HttpStatusCode.PreconditionFailed));
    }

    /// <summary>
    /// Twenty parallel advances of an exhausted queue end the session once, and never resurrect it.
    /// </summary>
    [Fact]
    public async Task Advance_TwentyParallelAdvancesOfALastTrack_EndItOnceAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "advance-end-race@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "One track only");
        Guid only = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        string shared = await PlaybackRequests.ETagAsync(a, started.SessionId);

        // Act
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async _ =>
            {
                using HttpResponseMessage response = await PlaybackRequests.AdvanceAsync(
                    a, started.SessionId, only, shared);

                return response.StatusCode;
            }));

        // Assert — one `409` ended it; the rest found a session that was already over.
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.Conflict));

        PlaybackRequests.SessionBody ended = await PlaybackRequests.SessionAsync(a, started.SessionId);
        Assert.NotNull(ended.EndedAtUtc);
        Assert.Null(ended.NowPlayingId);
    }

    /// <summary>
    /// An advance reaches a client listening to the source — `NowPlayingChanged` on the hub that owns it.
    /// </summary>
    [Fact]
    public async Task Advance_ReachesAJoinedClient_AsNowPlayingChangedAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "advance-broadcast@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Broadcasting");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        await using HubConnection listener = HubClients.Build(_factory, pair.AccessToken);
        await listener.StartAsync(TestContext.Current.CancellationToken);
        await listener.InvokeAsync(
            PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);

        TaskCompletionSource<NowPlayingEvent> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.On<NowPlayingEvent>(
            "NowPlayingChanged",
            message =>
            {
                if (message.NowPlayingId == second)
                {
                    received.TrySetResult(message);
                }
            });

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);
        Assert.Equal(first, started.NowPlayingId);

        // Act
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            a, started.SessionId, first, await PlaybackRequests.ETagAsync(a, started.SessionId));
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        // Assert
        NowPlayingEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(playlistId, message.SourceId);
        Assert.Equal(started.SessionId, message.SessionId);
    }

    /// <summary>`NowPlayingChanged`, as a client reads it.</summary>
    /// <param name="SourceId">The event or playlist the listeners are grouped by.</param>
    /// <param name="SessionId">The session that moved.</param>
    /// <param name="NowPlayingId">What is playing now, or null once the queue is exhausted.</param>
    internal sealed record NowPlayingEvent(Guid SourceId, Guid SessionId, Guid? NowPlayingId);
}
