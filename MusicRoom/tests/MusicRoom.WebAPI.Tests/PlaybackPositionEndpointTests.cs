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
/// The playing device reporting where it is — SPEC-059.
/// </summary>
/// <remarks>
/// Three claims are load-bearing and each has a test: a report leaves the session's version alone, a
/// report for a track that has moved on changes nothing, and no audit row is written. The first is why
/// the position lives in its own table; the third is a decision about subject V.6 rather than an
/// oversight, so it is asserted rather than assumed.
/// </remarks>
public sealed class PlaybackPositionEndpointTests : IAsyncLifetime
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

    /// <summary>The controller reports, and everybody reading the session sees it — SPEC-059.</summary>
    [Fact]
    public async Task Report_FromTheController_IsVisibleOnTheSessionAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "speaker@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        // Act
        using HttpResponseMessage reported = await ReportAsync(host, sessionId, nowPlayingId, 83500, paused: false);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, reported.StatusCode);

        PlaybackRequests.SessionBody session = await PlaybackRequests.SessionAsync(host, sessionId);

        Assert.Equal(83500, session.PositionMs);
        Assert.False(session.Paused);
        Assert.NotNull(session.PositionAtUtc);
    }

    /// <summary>
    /// Ten reports leave the session's `ETag` exactly as it was — SPEC-059 decision 1.
    /// </summary>
    /// <remarks>
    /// The reason the position has its own table. On the session row, a report every fifteen seconds
    /// would bump `xmin` four times a minute and answer every controller holding an `If-Match` with
    /// `412` — a working client turned into a `412` generator by a value it never asked about.
    /// </remarks>
    [Fact]
    public async Task Report_DoesNotMoveTheSessionsETagAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "etag@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        string before = await PlaybackRequests.ETagAsync(host, sessionId);

        // Act
        for (int report = 0; report < 10; report++)
        {
            using HttpResponseMessage response = await ReportAsync(
                host, sessionId, nowPlayingId, 1000 * report, paused: false);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Assert — and the advance that follows still works, which is the thing the ETag is for.
        string after = await PlaybackRequests.ETagAsync(host, sessionId);
        Assert.Equal(before, after);

        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            host, sessionId, nowPlayingId, after);
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);
    }

    /// <summary>A report for a track that has moved on changes nothing — SPEC-059 decision 6.</summary>
    /// <remarks>
    /// The ping was in flight while the session advanced. Applying it would make every listener seek
    /// backwards into the previous song.
    /// </remarks>
    [Fact]
    public async Task Report_ForATrackThatMovedOn_IsDiscardedAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "stale@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid firstTrack) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        string etag = await PlaybackRequests.ETagAsync(host, sessionId);
        using HttpResponseMessage advanced = await PlaybackRequests.AdvanceAsync(
            host, sessionId, firstTrack, etag);
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        // Act — the report names the track that was playing a moment ago.
        using HttpResponseMessage late = await ReportAsync(host, sessionId, firstTrack, 90000, paused: false);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);

        PlaybackRequests.SessionBody session = await PlaybackRequests.SessionAsync(host, sessionId);
        Assert.Null(session.PositionMs);
    }

    /// <summary>A guest cannot report, and is told what a stranger is told — SPEC-059 decision 4.</summary>
    [Fact]
    public async Task Report_FromSomebodyWhoDoesNotControlTheDevice_Returns404Async()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "owner@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        (HttpClient guestClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "guest@example.com");
        using HttpClient guest = guestClient;

        // Act
        using HttpResponseMessage refused = await ReportAsync(guest, sessionId, nowPlayingId, 1000, paused: false);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        PlaybackRequests.SessionBody session = await PlaybackRequests.SessionAsync(host, sessionId);
        Assert.Null(session.PositionMs);
    }

    /// <summary>
    /// Fifty reports leave no audit records — SPEC-059 decision 3, and a claim about subject V.6.
    /// </summary>
    /// <remarks>
    /// V.6 asks for a record of what a person did in the app. A timer is not a person, and four rows a
    /// minute per session would bury the records V.6 exists to produce. The command does not implement
    /// `IAuditableCommand`, which is the mechanism that marker was built for — and this is the test that
    /// stops somebody adding it "for consistency".
    /// </remarks>
    [Fact]
    public async Task Report_WritesNoAuditRecordAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "quiet@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        int before = await CountAuditRecordsAsync();

        // Act
        for (int report = 0; report < 50; report++)
        {
            using HttpResponseMessage response = await ReportAsync(
                host, sessionId, nowPlayingId, 500 * report, paused: false);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Assert
        Assert.Equal(before, await CountAuditRecordsAsync());
    }

    /// <summary>A paused report says so, and the flag survives the round trip.</summary>
    [Fact]
    public async Task Report_WhenPaused_SaysSoOnTheSessionAsync()
    {
        // Arrange
        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, "paused@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        // Act
        using HttpResponseMessage reported = await ReportAsync(host, sessionId, nowPlayingId, 12000, paused: true);
        Assert.Equal(HttpStatusCode.NoContent, reported.StatusCode);

        // Assert — a listener reads the flag, not only the number: a paused position does not advance.
        PlaybackRequests.SessionBody session = await PlaybackRequests.SessionAsync(host, sessionId);

        Assert.Equal(12000, session.PositionMs);
        Assert.True(session.Paused);
    }

    /// <summary>
    /// Two reports in one second: the second is refused — SPEC-059 decision 5.
    /// </summary>
    /// <remarks>
    /// This suite raises the limit so that the other tests measure the route rather than the limiter, so
    /// this one builds a host at the production number — one a second, partitioned by session. A party's
    /// speaker sends four times slower than that; anything faster is a client that is wrong, and the
    /// mobile side already treats a refusal as something to drop rather than retry.
    /// </remarks>
    [Fact]
    public async Task Report_TwiceInOneSecond_IsRefusedAsync()
    {
        // Arrange — a second host, with the position limit left at its production value.
        using WebApplicationFactory<Program> limited = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
                TestJwtSettings.Apply(webHost);
                TestMailSettings.Apply(webHost);
                TestRateLimitSettings.Relax(webHost);
                // The production number, written out rather than read from the policy: a test that
                // asserts against the value it is testing cannot notice it change.
                webHost.UseSetting("RATE_LIMIT_PLAYBACK_POSITION_PERMITS", "1");
                webHost.ConfigureTestServices(services => services.AddSingleton<IMailService>(_mail));
            });

        (HttpClient hostClient, TokenPair _) = await TestBearer.SignInAsync(limited, _mail, "fast@example.com");
        using HttpClient host = hostClient;

        (Guid _, Guid _, Guid sessionId, Guid nowPlayingId) =
            await PlaybackSessionEndpointTests.LiveEventSessionAsync(host);

        // Act
        using HttpResponseMessage first = await ReportAsync(host, sessionId, nowPlayingId, 1000, paused: false);
        using HttpResponseMessage second = await ReportAsync(host, sessionId, nowPlayingId, 1500, paused: false);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private static async Task<HttpResponseMessage> ReportAsync(
        HttpClient client,
        Guid sessionId,
        Guid nowPlayingId,
        int positionMs,
        bool paused) =>
        await client.PutAsJsonAsync(
            new Uri($"/api/v1/playback-sessions/{sessionId}/position", UriKind.Relative),
            new { nowPlayingId, positionMs, paused },
            TestContext.Current.CancellationToken);

    private async Task<int> CountAuditRecordsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords.AsNoTracking().CountAsync(TestContext.Current.CancellationToken);
    }
}
