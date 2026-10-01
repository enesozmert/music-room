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
/// Playlists: creation, the ETag and the If-Match guard — SPEC-014, subject V.2.3.
/// </summary>
/// <remarks>
/// The `ETag` and `If-Match` tests are the ones that matter: they are the guard that makes a concurrent edit
/// fail loudly instead of silently overwriting, which is what subject V.2.3 grades.
/// </remarks>
public sealed class PlaylistEndpointTests : IAsyncLifetime
{
    internal const string PlaylistsPath = "/api/v1/playlists";

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

    /// <summary>The aggregate exists, and its owner can read it back.</summary>
    [Fact]
    public async Task Create_ThenRead_ReturnsThePlaylistToItsOwnerAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "playlist-owner@example.com");

        using HttpClient a = owner;

        // Act
        Guid playlistId = await CreateAsync(a, "Road trip");

        using HttpResponseMessage read = await a.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        PlaylistBody? body = await read.Content.ReadFromJsonAsync<PlaylistBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Road trip", body.Name);
        Assert.True(body.MayEdit);
        Assert.NotNull(read.Headers.ETag);
    }
    /// <summary>The event licence is refused on a playlist — SPEC-014 decision 4.</summary>
    [Fact]
    public async Task Create_WithTheLocationAndTimeLicence_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "geofenced-playlist@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await a.PostAsJsonAsync(
            PlaylistsPath,
            new
            {
                name = "Geofenced",
                visibility = Visibility.Private,
                licence = LicenceMode.LocationAndTimeRestricted,
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
    /// <summary>The token is stable while the row is unchanged.</summary>
    [Fact]
    public async Task Get_TwiceUnchanged_ReturnsTheSameETagAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "stable-etag@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Stable");

        // Act
        string first = await ETagAsync(a, playlistId);
        string second = await ETagAsync(a, playlistId);

        // Assert
        Assert.Equal(first, second);
    }
    /// <summary>A fresh token succeeds and the token changes, so the next stale write fails.</summary>
    [Fact]
    public async Task Patch_WithAFreshIfMatch_Succeeds_AndTheETagChangesAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "fresh-etag@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Before");
        string etag = await ETagAsync(a, playlistId);

        // Act
        using HttpResponseMessage renamed = await PatchAsync(a, playlistId, "After", etag);

        // Assert
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.NotEqual(etag, await ETagAsync(a, playlistId));
    }
    /// <summary>
    /// A stale token changes nothing and answers `412` — the failure subject V.2.3 grades, made loud.
    /// </summary>
    [Fact]
    public async Task Patch_WithAStaleIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "stale-etag@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Original");
        string stale = await ETagAsync(a, playlistId);

        using HttpResponseMessage firstWrite = await PatchAsync(a, playlistId, "First writer", stale);
        Assert.Equal(HttpStatusCode.OK, firstWrite.StatusCode);

        // Act — the second writer still holds the token from before the first write.
        using HttpResponseMessage secondWrite = await PatchAsync(a, playlistId, "Second writer", stale);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, secondWrite.StatusCode);

        PlaylistBody? body = await ReadAsync(a, playlistId);
        Assert.NotNull(body);
        Assert.Equal("First writer", body.Name);
    }
    /// <summary>No `If-Match` is treated as a mismatch: the contract lists `412` and not `428`.</summary>
    [Fact]
    public async Task Patch_WithNoIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "no-etag@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Unguarded");

        // Act
        using HttpResponseMessage response = await a.PatchAsJsonAsync(
            $"{PlaylistsPath}/{playlistId}",
            new { name = "Sneaky" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }
    /// <summary>Creating a playlist is audited — ADR-0033 on a new surface.</summary>
    [Fact]
    public async Task Create_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "audited-playlist@example.com");

        using HttpClient a = owner;

        // Act
        Guid playlistId = await CreateAsync(a, "Audited");

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "playlist.create",
                TestContext.Current.CancellationToken);

        Assert.NotNull(record.UserId);
        Assert.Equal(playlistId, record.TargetId);
        Assert.Equal("playlist", record.TargetType);
    }

    internal static async Task<Guid> CreateAsync(
        HttpClient client,
        string name,
        Visibility visibility = Visibility.Private,
        LicenceMode licence = LicenceMode.InvitedOnly)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            PlaylistsPath,
            new { name, visibility, licence },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedBody? created = await response.Content.ReadFromJsonAsync<CreatedBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        return created.PlaylistId;
    }

    private static async Task<PlaylistBody?> ReadAsync(HttpClient client, Guid playlistId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<PlaylistBody>(TestContext.Current.CancellationToken);
    }

    private static async Task<string> ETagAsync(HttpClient client, Guid playlistId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        return response.Headers.ETag.Tag;
    }

    private static async Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        Guid playlistId,
        string name,
        string etag)
    {
        using HttpRequestMessage request = new(HttpMethod.Patch, $"{PlaylistsPath}/{playlistId}")
        {
            Content = JsonContent.Create(new { name }),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>What `POST /playlists` answers with.</summary>
    /// <param name="PlaylistId">The new playlist.</param>
    internal sealed record CreatedBody(Guid PlaylistId);

    /// <summary>One playlist, as a client reads it.</summary>
    /// <param name="PlaylistId">The playlist.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="MayEdit">Whether this caller may write to it.</param>
    internal sealed record PlaylistBody(Guid PlaylistId, string Name, bool MayEdit);

}
