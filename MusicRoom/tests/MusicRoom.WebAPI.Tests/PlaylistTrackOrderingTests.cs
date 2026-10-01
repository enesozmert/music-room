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
/// Where a track lands: the head, the tail, next to a neighbour, and after a renumber — SPEC-015.
/// </summary>
/// <remarks>
/// Positions are sparse so an insert writes one row. The test that matters most here is the repeated insert
/// into the same gap: it exhausts the gap, forces the renumber, and asserts that the **order** survived it.
/// A reorder scheme that loses the order under renumbering is worse than no scheme at all.
/// </remarks>
public sealed class PlaylistTrackOrderingTests : IAsyncLifetime
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

    /// <summary>With no destination named, a track is appended.</summary>
    [Fact]
    public async Task AddTrack_WithNoDestination_AppendsAsync()
    {
        // Arrange
        using HttpClient a = await SignInAsync("append-track@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Appending");

        // Act
        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        // Assert
        Assert.Equal([first, second], await OrderAsync(a, playlistId));
    }

    /// <summary>`atHead` puts a track before everything.</summary>
    [Fact]
    public async Task AddTrack_AtHead_InsertsBeforeEverythingAsync()
    {
        // Arrange
        using HttpClient a = await SignInAsync("head-track@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Head first");

        Guid existing = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            a,
            playlistId,
            new { youtubeVideoId = "bbbbbbbbbbb", atHead = true },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(existing, (await OrderAsync(a, playlistId))[1]);
    }

    /// <summary>`afterTrackId` puts a track between two others.</summary>
    [Fact]
    public async Task AddTrack_AfterANamedTrack_InsertsBetweenAsync()
    {
        // Arrange
        using HttpClient a = await SignInAsync("between-track@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Between");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid last = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            a,
            playlistId,
            new { youtubeVideoId = "bbbbbbbbbbb", afterTrackId = first },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        IReadOnlyList<Guid> order = await OrderAsync(a, playlistId);
        Assert.Equal(3, order.Count);
        Assert.Equal(first, order[0]);
        Assert.Equal(last, order[2]);
    }

    /// <summary>A neighbour that is not in this playlist is `404`, and nothing is added.</summary>
    [Fact]
    public async Task AddTrack_AfterAnUnknownTrack_Returns404Async()
    {
        // Arrange
        using HttpClient a = await SignInAsync("unknown-neighbour@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "No such neighbour");
        string etag = await TrackRequests.ETagAsync(a, playlistId);

        // Act
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            a,
            playlistId,
            new { youtubeVideoId = "aaaaaaaaaaa", afterTrackId = Guid.NewGuid() },
            etag);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await TrackRequests.ListAsync(a, playlistId));

        // The refusal left the collection's token alone, so the client's next write still works.
        Assert.Equal(etag, await TrackRequests.ETagAsync(a, playlistId));
    }

    /// <summary>A move before a named track reorders the list.</summary>
    [Fact]
    public async Task MoveTrack_BeforeTheFirst_PutsItFirstAsync()
    {
        // Arrange
        using HttpClient a = await SignInAsync("move-before@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Reordered");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        // Act
        using HttpResponseMessage response = await TrackRequests.PatchTrackAsync(
            a,
            playlistId,
            second,
            new { beforeTrackId = first },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([second, first], await OrderAsync(a, playlistId));
    }

    /// <summary>Both neighbours at once is `422`: the contract's own rule.</summary>
    [Fact]
    public async Task MoveTrack_WithBothNeighbours_Returns422Async()
    {
        // Arrange
        using HttpClient a = await SignInAsync("both-neighbours@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Ambiguous");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");
        Guid third = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        // Act
        using HttpResponseMessage response = await TrackRequests.PatchTrackAsync(
            a,
            playlistId,
            third,
            new { afterTrackId = first, beforeTrackId = second },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>No destination at all is `422`: a move with nowhere to go is not a move.</summary>
    [Fact]
    public async Task MoveTrack_WithNoNeighbour_Returns422Async()
    {
        // Arrange
        using HttpClient a = await SignInAsync("no-neighbour@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Nowhere");

        Guid only = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        // Act
        using HttpResponseMessage response = await TrackRequests.PatchTrackAsync(
            a,
            playlistId,
            only,
            new { },
            await TrackRequests.ETagAsync(a, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// Eleven inserts into the same gap exhaust it, force the renumber, and leave the order intact.
    /// </summary>
    /// <remarks>
    /// A gap of 1000 survives ten halvings; the eleventh has nothing left to halve. That is the branch the
    /// sparse scheme is judged on, because it is the one that only fires after the feature has been used.
    /// </remarks>
    [Fact]
    public async Task AddTrack_RepeatedlyIntoOneGap_RenumbersAndKeepsTheOrderAsync()
    {
        // Arrange
        using HttpClient a = await SignInAsync("exhausted-gap@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Exhausted");

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid last = await TrackRequests.AddAsync(a, playlistId, "zzzzzzzzzzz");

        List<Guid> inserted = [];

        // Act — always into the gap directly after the first track, so each insert halves what is left.
        for (int round = 0; round < 11; round++)
        {
            using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
                a,
                playlistId,
                new { youtubeVideoId = "mmmmmmmmmmm", afterTrackId = first },
                await TrackRequests.ETagAsync(a, playlistId));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            TrackRequests.AddedBody? added = await response.Content
                .ReadFromJsonAsync<TrackRequests.AddedBody>(TestContext.Current.CancellationToken);
            Assert.NotNull(added);
            inserted.Insert(0, added.PlaylistTrackId);
        }

        // Assert — the first track, then the inserts newest-first, then the original tail.
        Assert.Equal([first, .. inserted, last], await OrderAsync(a, playlistId));
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        return client;
    }

    private static async Task<IReadOnlyList<Guid>> OrderAsync(HttpClient client, Guid playlistId) =>
        [.. (await TrackRequests.ListAsync(client, playlistId)).Select(track => track.PlaylistTrackId)];
}
