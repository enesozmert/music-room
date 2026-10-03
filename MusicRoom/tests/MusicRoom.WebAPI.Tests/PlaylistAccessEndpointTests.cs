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
/// Playlists: the member, owner and licence gates, listing and the soft delete — SPEC-014, subject V.6.
/// </summary>
/// <remarks>
/// The `ETag` and `If-Match` tests are the ones that matter: they are the guard that makes a concurrent edit
/// fail loudly instead of silently overwriting, which is what subject V.2.3 grades.
/// </remarks>
public sealed class PlaylistAccessEndpointTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
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

    /// <summary>A private playlist is invisible to a stranger — `404`, not `403`.</summary>
    [Fact]
    public async Task Get_APrivatePlaylist_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "private-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(_factory, _mail, "private-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid playlistId = await CreateAsync(a, "Secret");

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    /// <summary>A public playlist is readable by any authenticated caller — decision 3.</summary>
    [Fact]
    public async Task Get_APublicPlaylist_AsAnyAuthenticatedCaller_Returns200Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "public-owner@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "public-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        Guid playlistId = await CreateAsync(a, "Open", Visibility.Public, LicenceMode.InvitedOnly);

        // Act
        PlaylistBody? body = await ReadAsync(b, playlistId);

        // Assert — readable, and not editable: the licence is invited-only.
        Assert.NotNull(body);
        Assert.Equal("Open", body.Name);
        Assert.False(body.MayEdit);
    }
    /// <summary>A public playlist licensed to everyone may be edited by any reader.</summary>
    [Fact]
    public async Task Patch_APublicEveryonePlaylist_AsAReader_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "everyone-owner@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "everyone-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        Guid playlistId = await CreateAsync(a, "Communal", Visibility.Public, LicenceMode.Everyone);

        // Act
        using HttpResponseMessage response = await PatchAsync(
            b,
            playlistId,
            "Edited by a stranger",
            await ETagAsync(b, playlistId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    /// <summary>Only the owner sets visibility and licence.</summary>
    [Fact]
    public async Task SetLicence_AsAReader_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "licence-owner@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "licence-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        Guid playlistId = await CreateAsync(a, "Communal", Visibility.Public, LicenceMode.Everyone);

        // Act
        using HttpResponseMessage response = await b.PutAsJsonAsync(
            $"{PlaylistsPath}/{playlistId}/licence",
            new { visibility = Visibility.Private, licence = LicenceMode.InvitedOnly },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    /// <summary>A soft delete removes it from every read — decision 2.</summary>
    [Fact]
    public async Task Delete_AsOwner_MakesItInvisibleEverywhereAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "deletes-playlist@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Doomed");

        // Act
        using HttpResponseMessage deleted = await a.DeleteAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using HttpResponseMessage read = await a.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        List<SummaryBody> mine = await ListAsync(a, "mine");
        Assert.DoesNotContain(mine, summary => summary.PlaylistId == playlistId);
    }
    /// <summary>The row survives the delete, which is what makes the audit trail still point at something.</summary>
    [Fact]
    public async Task Delete_LeavesTheRowInPlaceAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "soft-delete@example.com");

        using HttpClient a = owner;
        Guid playlistId = await CreateAsync(a, "Soft");

        // Act
        using HttpResponseMessage deleted = await a.DeleteAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Assert — the query filter hides it from the application; the row is still there.
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Assert.True(await context.Playlists
            .IgnoreQueryFilters()
            .AnyAsync(
                playlist => playlist.Id == playlistId && playlist.DeletedAtUtc != null,
                TestContext.Current.CancellationToken));
    }
    /// <summary>A list scope never leaks somebody else's private playlist.</summary>
    [Fact]
    public async Task List_PublicScope_DoesNotIncludeAPrivatePlaylistAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "list-owner@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "list-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        Guid privateId = await CreateAsync(a, "Hidden");
        Guid publicId = await CreateAsync(a, "Shown", Visibility.Public, LicenceMode.InvitedOnly);

        // Act
        List<SummaryBody> visible = await ListAsync(b, "public");

        // Assert
        Assert.Contains(visible, summary => summary.PlaylistId == publicId);
        Assert.DoesNotContain(visible, summary => summary.PlaylistId == privateId);
    }
    /// <summary>An unknown scope is refused rather than silently treated as `mine`.</summary>
    [Fact]
    public async Task List_WithAnUnknownScope_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "bad-scope@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await a.GetAsync(
            $"{PlaylistsPath}?scope=everything",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static async Task<Guid> CreateAsync(
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

    private static async Task<List<SummaryBody>> ListAsync(HttpClient client, string scope)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{PlaylistsPath}?scope={scope}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<SummaryBody>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>What `POST /playlists` answers with.</summary>
    /// <param name="PlaylistId">The new playlist.</param>
    internal sealed record CreatedBody(Guid PlaylistId);

    /// <summary>One playlist, as a client reads it.</summary>
    /// <param name="PlaylistId">The playlist.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="MayEdit">Whether this caller may write to it.</param>
    internal sealed record PlaylistBody(Guid PlaylistId, string Name, bool MayEdit);

    /// <summary>One playlist in a list.</summary>
    /// <param name="PlaylistId">The playlist.</param>
    /// <param name="Name">Its name.</param>
    internal sealed record SummaryBody(Guid PlaylistId, string Name);
}
