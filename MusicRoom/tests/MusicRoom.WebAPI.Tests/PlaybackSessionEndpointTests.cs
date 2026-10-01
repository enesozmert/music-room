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
/// Opening, reading and stopping a playback session — SPEC-021, ADR-0017.
/// </summary>
/// <remarks>
/// The server picks what plays first, which is the half of subject V.2.1 the vote count exists for. The gates are
/// the other half of the suite: a session must not become a way to read a private source, and the device's owner
/// is the one who starts and stops playback.
/// </remarks>
public sealed class PlaybackSessionEndpointTests : IAsyncLifetime
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

    /// <summary>An event session starts on the most-voted suggestion — the server picks, not the client.</summary>
    [Fact]
    public async Task Start_OnAnEvent_PlaysTheTopOfTheQueueAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-event@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid eventId = await EventRequests.CreateAsync(
            a, "Live set", Visibility.Public, LicenceMode.Everyone);

        Guid unpopular = await EventQueueRequests.SuggestAsync(a, eventId, "aaaaaaaaaaa");
        Guid favourite = await EventQueueRequests.SuggestAsync(a, eventId, "bbbbbbbbbbb");

        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, favourite);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Act
        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Event, eventId);

        // Assert — the voted track, not the first suggested one.
        Assert.Equal(favourite, started.NowPlayingId);
        Assert.NotEqual(unpopular, started.NowPlayingId);
    }

    /// <summary>A playlist session starts on the first track by position.</summary>
    [Fact]
    public async Task Start_OnAPlaylist_PlaysTheFirstTrackAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-playlist@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Road trip");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        // Act
        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Assert
        Assert.Equal(first, started.NowPlayingId);
    }

    /// <summary>A device holds at most one live session — the filtered unique index says so.</summary>
    [Fact]
    public async Task Start_TwiceOnOneDevice_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-twice@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "One at a time");
        await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        await PlaybackRequests.StartAsync(a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Act
        using HttpResponseMessage second = await PlaybackRequests.PostStartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Playback starts on the caller's own device and nobody else's.</summary>
    [Fact]
    public async Task Start_OnAnotherAccountsDevice_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-device-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(
            _factory, _mail, "playback-device-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(b, "Theirs");

        // Act
        using HttpResponseMessage response = await PlaybackRequests.PostStartAsync(
            b, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A session cannot be opened on a source the caller may not see.</summary>
    [Fact]
    public async Task Start_OnASourceTheCallerMayNotSee_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-source-owner@example.com");
        (HttpClient other, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-source-other@example.com");

        using HttpClient a = owner;
        using HttpClient b = other;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // A private playlist belonging to somebody else.
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(b, "Private", Visibility.Private);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.PostStartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An empty source still opens a session, with nothing playing.</summary>
    [Fact]
    public async Task Start_OnAnEmptySource_StartsWithNothingPlayingAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-empty@example.com");

        using HttpClient a = owner;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid eventId = await EventRequests.CreateAsync(
            a, "Nobody has suggested anything", Visibility.Public, LicenceMode.Everyone);

        // Act
        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Event, eventId);

        // Assert — a room that starts the music before anybody suggested a track is waiting, not broken.
        Assert.Null(started.NowPlayingId);
    }

    /// <summary>Anybody who may see the source may see what it is playing, with an `ETag`.</summary>
    [Fact]
    public async Task Get_AsAMemberOfTheSource_Returns200Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-read-owner@example.com");
        (HttpClient listener, _) = await TestBearer.SignInAsync(
            _factory, _mail, "playback-read-listener@example.com");

        using HttpClient a = owner;
        using HttpClient b = listener;

        (Guid deviceId, Guid eventId, Guid sessionId, _) = await LiveEventSessionAsync(a);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.GetAsync(b, sessionId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(Guid.Empty, deviceId);
        Assert.NotEqual(Guid.Empty, eventId);
    }

    /// <summary>A private source's session is invisible.</summary>
    [Fact]
    public async Task Get_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-hidden-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(
            _factory, _mail, "playback-hidden-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Private", Visibility.Private);
        await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            a, deviceId, PlaybackSourceType.Playlist, playlistId);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.GetAsync(b, started.SessionId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The device's owner can ask what it is playing.</summary>
    [Fact]
    public async Task GetByDevice_AsTheOwner_Returns200Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-by-device@example.com");

        using HttpClient a = owner;
        (Guid deviceId, _, Guid sessionId, _) = await LiveEventSessionAsync(a);

        // Act
        using HttpResponseMessage response = await a.GetAsync(
            $"{DeviceRequests.DevicesPath}/{deviceId}/playback-session",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlaybackRequests.SessionBody? body =
            await response.Content.ReadFromJsonAsync<PlaybackRequests.SessionBody>(
                TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(sessionId, body.SessionId);
    }

    /// <summary>A device that is not playing has no session to read.</summary>
    [Fact]
    public async Task GetByDevice_WithNoSession_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-silent@example.com");

        using HttpClient a = owner;
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await a.GetAsync(
            $"{DeviceRequests.DevicesPath}/{deviceId}/playback-session",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Stopping ends the session, and the device may start another — decision 2's point.</summary>
    [Fact]
    public async Task Stop_AsTheDeviceOwner_EndsIt_AndAllowsANewSessionAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-stop@example.com");

        using HttpClient a = owner;
        (Guid deviceId, Guid eventId, Guid sessionId, _) = await LiveEventSessionAsync(a);

        // Act
        using HttpResponseMessage stopped = await PlaybackRequests.StopAsync(a, sessionId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);

        PlaybackRequests.SessionBody ended = await PlaybackRequests.SessionAsync(a, sessionId);
        Assert.NotNull(ended.EndedAtUtc);
        Assert.Null(ended.NowPlayingId);

        // The filtered index let the history stay, so a new session is allowed.
        using HttpResponseMessage again = await PlaybackRequests.PostStartAsync(
            a, deviceId, PlaybackSourceType.Event, eventId);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    /// <summary>Only the device's owner stops playback.</summary>
    [Fact]
    public async Task Stop_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-stop-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(
            _factory, _mail, "playback-stop-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        (_, _, Guid sessionId, _) = await LiveEventSessionAsync(a);

        // Act
        using HttpResponseMessage response = await PlaybackRequests.StopAsync(b, sessionId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Starting playback is audited — ADR-0033 on a new surface.</summary>
    [Fact]
    public async Task Start_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playback-audited@example.com");

        using HttpClient a = owner;

        // Act
        (_, _, Guid sessionId, _) = await LiveEventSessionAsync(a);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "playback.start",
                TestContext.Current.CancellationToken);

        Assert.Equal(sessionId, record.TargetId);
        Assert.Equal("playback-session", record.TargetType);
    }

    /// <summary>A device, a public event with two voted suggestions, and a live session on it.</summary>
    internal static async Task<(Guid DeviceId, Guid EventId, Guid SessionId, Guid NowPlayingId)>
        LiveEventSessionAsync(HttpClient owner)
    {
        Guid deviceId = await DeviceRequests.RegisterAsync(owner);
        Guid eventId = await EventRequests.CreateAsync(
            owner, "Live set", Visibility.Public, LicenceMode.Everyone);

        Guid first = await EventQueueRequests.SuggestAsync(owner, eventId, "aaaaaaaaaaa");
        await EventQueueRequests.SuggestAsync(owner, eventId, "bbbbbbbbbbb");

        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(owner, eventId, first);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        PlaybackRequests.StartedBody started = await PlaybackRequests.StartAsync(
            owner, deviceId, PlaybackSourceType.Event, eventId);

        Assert.NotNull(started.NowPlayingId);

        return (deviceId, eventId, started.SessionId, started.NowPlayingId.Value);
    }
}
