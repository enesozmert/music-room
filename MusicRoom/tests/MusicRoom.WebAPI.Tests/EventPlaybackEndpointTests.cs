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
/// What an event is playing, read by a guest — SPEC-058.
/// </summary>
/// <remarks>
/// The half that matters here is the privacy one: this route must not become a way to learn that a
/// private event exists or that music is on in it. The other half is the stamp, which is the number a
/// guest arriving mid-track seeks to.
/// </remarks>
public sealed class EventPlaybackEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services => services.AddSingleton<IMailService>(_mail));
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

    /// <summary>A guest who may see the event reads what it is playing, and from when — SPEC-058.</summary>
    [Fact]
    public async Task Get_ForAGuestOfAPublicEvent_ReturnsTheLiveSessionAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "host@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid eventId, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        (HttpClient guestClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "guest@example.com");
        using HttpClient guest = guestClient;

        // Act
        using HttpResponseMessage response = await guest.GetAsync(
            new Uri($"/api/v1/events/{eventId}/playback-session", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlaybackRequests.SessionBody? body = await response.Content
            .ReadFromJsonAsync<PlaybackRequests.SessionBody>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(sessionId, body.SessionId);
        Assert.Equal(nowPlayingId, body.NowPlayingId);

        // The stamp is what a guest arriving mid-track seeks to, and the clock is what lets them correct
        // for their own — SPEC-058 decision 4.
        Assert.NotNull(body.NowPlayingStartedAtUtc);
        Assert.InRange(
            body.ServerNowUtc,
            body.NowPlayingStartedAtUtc.Value,
            body.NowPlayingStartedAtUtc.Value.AddMinutes(1));

        // The ETag a command sends back as If-Match, as the session route publishes it.
        Assert.NotNull(response.Headers.ETag);
    }

    /// <summary>
    /// A stranger to a private event is told the same thing as somebody asking about an event with no
    /// music on: nothing.
    /// </summary>
    [Fact]
    public async Task Get_ForAStrangerToAPrivateEvent_AnswersLikeAnEventWithNoSessionAsync()
    {
        // Arrange — a private event with music playing, and a public one with none.
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "private@example.com");
        using HttpClient host = hostClient;

        Guid deviceId = await DeviceRequests.RegisterAsync(host);
        Guid privateEvent = await EventRequests.CreateAsync(
            host, "Closed doors", Visibility.Private, LicenceMode.Everyone);
        await EventQueueRequests.SuggestAsync(host, privateEvent, "aaaaaaaaaaa");
        await PlaybackRequests.StartAsync(host, deviceId, PlaybackSourceType.Event, privateEvent);

        Guid silentEvent = await EventRequests.CreateAsync(
            host, "Nothing on", Visibility.Public, LicenceMode.Everyone);

        (HttpClient strangerClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "stranger@example.com");
        using HttpClient stranger = strangerClient;

        // Act
        using HttpResponseMessage hidden = await stranger.GetAsync(
            new Uri($"/api/v1/events/{privateEvent}/playback-session", UriKind.Relative),
            TestContext.Current.CancellationToken);
        using HttpResponseMessage silent = await stranger.GetAsync(
            new Uri($"/api/v1/events/{silentEvent}/playback-session", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — same status, same body. The route is not a way to learn a private event is playing.
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(silent.StatusCode, hidden.StatusCode);
        Assert.Equal(
            await silent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await hidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The stamp moves with the track, and the session's own start does not — SPEC-058.</summary>
    [Fact]
    public async Task Advance_MovesTheStampAndLeavesTheSessionStartAloneAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "advancer@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid firstTrack) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        PlaybackRequests.SessionBody before = await PlaybackRequests.SessionAsync(host, sessionId);
        Assert.NotNull(before.NowPlayingStartedAtUtc);

        string etag = await PlaybackRequests.ETagAsync(host, sessionId);

        // Act
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            host, sessionId, firstTrack, etag);
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        // Assert
        PlaybackRequests.SessionBody after = await PlaybackRequests.SessionAsync(host, sessionId);

        Assert.NotNull(after.NowPlayingStartedAtUtc);
        Assert.NotEqual(before.NowPlayingId, after.NowPlayingId);
        Assert.True(
            after.NowPlayingStartedAtUtc > before.NowPlayingStartedAtUtc,
            "the stamp moves with the track it describes");
    }

    /// <summary>Two devices on one event: the newest live session answers — SPEC-058 decision 5.</summary>
    /// <remarks>
    /// The live-session index is per **device**, not per event, so two speakers in one room playing the
    /// same event is a state the system allows rather than one it refuses. A guest arriving now should
    /// follow the one that started last.
    /// <para>
    /// The second device belongs to a second account on purpose. A device is identified by the client
    /// headers that registered it, so one account registering twice from one client gets back the row it
    /// already had — which is the device contract working, not a second speaker.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Get_WithTwoDevicesPlayingOneEvent_AnswersTheNewestAsync()
    {
        // Arrange — a public event, so the second account may see it and play it.
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "two@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid eventId, Guid firstSession, Guid _) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        (HttpClient guestClient, TokenPair _) = await TestBearer.SignInAsync(
            _factory, _mail, "second-speaker@example.com");
        using HttpClient secondSpeaker = guestClient;

        Guid secondDevice = await DeviceRequests.RegisterAsync(secondSpeaker);
        PlaybackRequests.StartedBody second = await PlaybackRequests.StartAsync(
            secondSpeaker, secondDevice, PlaybackSourceType.Event, eventId);

        // Act
        using HttpResponseMessage response = await host.GetAsync(
            new Uri($"/api/v1/events/{eventId}/playback-session", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlaybackRequests.SessionBody? body = await response.Content
            .ReadFromJsonAsync<PlaybackRequests.SessionBody>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(second.SessionId, body.SessionId);
        Assert.NotEqual(firstSession, body.SessionId);
    }
}
