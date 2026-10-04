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
/// Twenty clients reordering one playlist at the same instant — AGENTS.md's parallel rule, on the write
/// subject V.2.3 grades.
/// </summary>
/// <remarks>
/// This is the suite SPEC-015 exists for. Every request carries the same `If-Match`, so by the contract exactly
/// one may be applied: the guarded `UPDATE` of the playlist row is evaluated under that row's lock, the winner
/// changes the version, and the other nineteen predicates no longer match. The assertion is on the **final
/// database state** rather than on counts alone, because a scheme that answers `412` nineteen times and still
/// corrupts the order has failed at the only thing it was for.
/// </remarks>
public sealed class PlaylistTrackConcurrencyTests : IAsyncLifetime
{
    /// <summary>How many clients race. Twenty is enough to lose every unguarded scheme.</summary>
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

    /// <summary>
    /// Twenty parallel moves of the same track with the same token: one `200`, nineteen `412`, and a final
    /// order that is still a permutation of the original.
    /// </summary>
    [Fact]
    public async Task Reorder_TwentyParallelMovesOfOneTrack_ProduceOneWinnerAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "reorder-race@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Contested order");

        List<Guid> original =
        [
            await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa"),
            await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb"),
            await TrackRequests.AddAsync(a, playlistId, "ccccccccccc"),
            await TrackRequests.AddAsync(a, playlistId, "ddddddddddd"),
        ];

        Guid moving = original[^1];
        string shared = await TrackRequests.ETagAsync(a, playlistId);

        // Act — every racer holds the same token and names a different destination.
        MoveRace race = new(playlistId, moving, shared);

        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers)
                .Select(index => MoveAsync(a, race, original[index % (original.Count - 1)])));

        // Assert
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(
            Racers - 1,
            answers.Count(status => status == HttpStatusCode.PreconditionFailed));

        IReadOnlyList<Guid> finalOrder =
            [.. (await TrackRequests.ListAsync(a, playlistId)).Select(track => track.PlaylistTrackId)];

        Assert.Equal(original.Count, finalOrder.Count);
        Assert.Equal(original.Order().ToArray(), finalOrder.Order().ToArray());

        // One winner means one write, so the positions are still distinct and the order is well defined.
        Assert.Equal(
            finalOrder.Count,
            (await TrackRequests.ListAsync(a, playlistId)).Select(track => track.Position).Distinct().Count());
    }

    /// <summary>
    /// Twenty parallel adds with the same token add exactly one track: the guard covers growth as well as
    /// reordering.
    /// </summary>
    [Fact]
    public async Task Add_TwentyParallelAddsWithOneToken_AddOneTrackAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "add-race@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Contested growth");
        string shared = await TrackRequests.ETagAsync(a, playlistId);

        // Act
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async index =>
            {
                using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
                    a,
                    playlistId,
                    new { youtubeVideoId = "aaaaaaaaaaa" },
                    shared);

                return response.StatusCode;
            }));

        // Assert
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.Created));
        Assert.Single(await TrackRequests.ListAsync(a, playlistId));
    }

    /// <summary>One racer's move. Every racer sends a different destination and the same token.</summary>
    private static async Task<HttpStatusCode> MoveAsync(HttpClient client, MoveRace race, Guid destination)
    {
        using HttpResponseMessage response = await TrackRequests.PatchTrackAsync(
            client,
            race.PlaylistId,
            race.Moving,
            new { afterTrackId = destination },
            race.ETag);

        return response.StatusCode;
    }

    /// <summary>What every racer in one reorder race shares.</summary>
    /// <param name="PlaylistId">The contested playlist.</param>
    /// <param name="Moving">The track every racer moves.</param>
    /// <param name="ETag">The one token they all hold, which is why only one of them may win.</param>
    private sealed record MoveRace(Guid PlaylistId, Guid Moving, string ETag);
}
