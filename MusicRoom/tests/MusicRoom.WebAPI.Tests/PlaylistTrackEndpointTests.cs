using System.Net;
using System.Net.Http.Headers;
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
/// Playlist tracks: adding, listing, removing, and the `If-Match` guard over a collection — SPEC-015.
/// </summary>
/// <remarks>
/// A collection carries no `ETag` of its own, so these tests pin the one decision that makes `If-Match` mean
/// anything here: the playlist's token changes when its **track list** changes, so a client that added a track
/// cannot then move one with the token it started from.
/// </remarks>
public sealed class PlaylistTrackEndpointTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string PlaylistsPath = "/api/v1/playlists";

    /// <summary>A well-formed YouTube video id, used where the id itself is not what is under test.</summary>
    internal const string SomeVideoId = "dQw4w9WgXcQ";

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

    /// <summary>A track added by the owner comes back in the list, with the id the `POST` reported.</summary>
    [Fact]
    public async Task AddTrack_ThenList_ReturnsItAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "track-owner@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "With a track");

        // Act
        Guid trackId = await TrackRequests.AddAsync(a, playlistId, SomeVideoId);
        IReadOnlyList<TrackRequests.TrackBody> tracks = await TrackRequests.ListAsync(a, playlistId);

        // Assert
        TrackRequests.TrackBody only = Assert.Single(tracks);
        Assert.Equal(trackId, only.PlaylistTrackId);
        Assert.Equal(SomeVideoId, only.YoutubeVideoId);
    }

    /// <summary>Adding a track changes the collection's `ETag`: that is what makes the guard a guard.</summary>
    [Fact]
    public async Task AddTrack_ChangesTheTrackListETagAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "track-etag@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Versioned");
        string before = await TrackRequests.ETagAsync(a, playlistId);

        // Act
        await TrackRequests.AddAsync(a, playlistId, SomeVideoId);

        // Assert
        Assert.NotEqual(before, await TrackRequests.ETagAsync(a, playlistId));
    }

    /// <summary>A stale token adds nothing and answers `412`.</summary>
    [Fact]
    public async Task AddTrack_WithAStaleIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "stale-track@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Contested");
        string stale = await TrackRequests.ETagAsync(a, playlistId);

        await TrackRequests.AddAsync(a, playlistId, SomeVideoId, stale);

        // Act — the second writer still holds the token from before the first add.
        using HttpResponseMessage second = await TrackRequests.PostTrackAsync(
            a, playlistId, new { youtubeVideoId = "9bZkp7q19f0" }, stale);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
        Assert.Single(await TrackRequests.ListAsync(a, playlistId));
    }

    /// <summary>No `If-Match` is a mismatch, as on the metadata `PATCH` — SPEC-014's rule, unchanged.</summary>
    [Fact]
    public async Task AddTrack_WithNoIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "unguarded-track@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Unguarded");

        // Act
        using HttpResponseMessage response = await a.PostAsJsonAsync(
            $"{PlaylistsPath}/{playlistId}/tracks",
            new { youtubeVideoId = SomeVideoId },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    /// <summary>The same video twice is allowed: a playlist that plays a track twice is a playlist.</summary>
    [Fact]
    public async Task AddTrack_TwiceWithTheSameVideo_IsAllowedAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "duplicate-track@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Twice");

        // Act
        await TrackRequests.AddAsync(a, playlistId, SomeVideoId);
        await TrackRequests.AddAsync(a, playlistId, SomeVideoId);

        // Assert
        Assert.Equal(2, (await TrackRequests.ListAsync(a, playlistId)).Count);
    }

    /// <summary>An id that is not a YouTube id is `422`, named — application-security.</summary>
    [Fact]
    public async Task AddTrack_WithAMalformedVideoId_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "bad-video-id@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Malformed");
        string etag = await TrackRequests.ETagAsync(a, playlistId);

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            a, playlistId, new { youtubeVideoId = "not a video" }, etag);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>A playlist the caller may not see answers `404`, never `403` — SPEC-014's rule.</summary>
    [Fact]
    public async Task ListTracks_ByAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "private-tracks@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(_factory, _mail, "track-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Private", Visibility.Private);
        await TrackRequests.AddAsync(a, playlistId, SomeVideoId);

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}/tracks",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A caller who may read a playlist but not write to it is refused with `404` — the `licence` gate from
    /// SPEC-014, holding on a new surface.
    /// </summary>
    /// <remarks>
    /// `404` and not `403`: the reader can already see the playlist, but the answer stays the one a stranger
    /// gets, so no route on this resource ever reveals whether a write was refused or the thing was missing.
    /// </remarks>
    [Fact]
    public async Task AddTrack_ByAReaderWhoMayNotEdit_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "licence-owner@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "licence-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        // Public so the reader may see it, InvitedOnly so only named editors may write.
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Read only", Visibility.Public);
        string etag = await TrackRequests.ETagAsync(b, playlistId);

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            b, playlistId, new { youtubeVideoId = SomeVideoId }, etag);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await TrackRequests.ListAsync(a, playlistId));
    }

    /// <summary>A removed track is gone, and the rest of the order is untouched.</summary>
    [Fact]
    public async Task RemoveTrack_LeavesTheRemainingOrderAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "remove-track@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Three then two");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");
        Guid third = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        // Act
        using HttpResponseMessage removed = await TrackRequests.DeleteTrackAsync(
            a, playlistId, second, await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal([first, third], (await TrackRequests.ListAsync(a, playlistId)).Select(track => track.PlaylistTrackId));
    }

    /// <summary>A playlist already holding its maximum answers `409` — SPEC-015 decision 4.</summary>
    /// <remarks>
    /// The rows are seeded through the context rather than through 500 requests: what is under test is the
    /// cap, not the add path, and 500 round trips would make this suite the slowest thing in the repository.
    /// </remarks>
    [Fact]
    public async Task AddTrack_ToAFullPlaylist_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "full-playlist@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Full");
        await FillAsync(playlistId);

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            a,
            playlistId,
            new { youtubeVideoId = SomeVideoId },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Adding a track is audited, against the playlist — ADR-0033 on a new surface.</summary>
    [Fact]
    public async Task AddTrack_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "audited-track@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Audited tracks");

        // Act
        await TrackRequests.AddAsync(a, playlistId, SomeVideoId);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "playlist.add-track",
                TestContext.Current.CancellationToken);

        Assert.NotNull(record.UserId);
        Assert.Equal(playlistId, record.TargetId);
        Assert.Equal("playlist", record.TargetType);
    }

    /// <summary>Seeds the playlist with as many tracks as it may hold.</summary>
    private async Task FillAsync(Guid playlistId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Guid addedBy = await context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Id == playlistId)
            .Select(playlist => playlist.OwnerId)
            .SingleAsync(TestContext.Current.CancellationToken);

        for (int index = 0; index < Domain.Entities.PlaylistTrack.MaximumTracksPerPlaylist; index++)
        {
            context.PlaylistTracks.Add(new Domain.Entities.PlaylistTrack
            {
                Id = Guid.NewGuid(),
                PlaylistId = playlistId,
                YoutubeVideoId = SomeVideoId,
                Position = (index + 1) * Domain.Entities.PlaylistTrack.PositionGap,
                AddedByUserId = addedBy,
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
