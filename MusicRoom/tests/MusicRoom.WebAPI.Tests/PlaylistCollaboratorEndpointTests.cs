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
/// Playlist collaborators — SPEC-014, subject V.2.3 and V.6.
/// </summary>
/// <remarks>
/// The interesting pair is `canEdit`: a collaborator without it may read and not write, which is the whole
/// reason the contract has both a `member` and a `licence` symbol.
/// </remarks>
public sealed class PlaylistCollaboratorEndpointTests : IAsyncLifetime
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

    /// <summary>A collaborator may read a private playlist they are named on.</summary>
    [Fact]
    public async Task AddCollaborator_LetsThemReadThePlaylistAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("collab-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("collab-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Shared");

        // Act
        using HttpResponseMessage added = await AddAsync(a, playlistId, friendId, canEdit: false);

        // Assert
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        using HttpResponseMessage read = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    /// <summary>Without `canEdit` a collaborator may read and not write — the two gates differ.</summary>
    [Fact]
    public async Task Patch_AsACollaboratorWithoutCanEdit_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("readonly-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("readonly-collab@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Read only");
        using HttpResponseMessage added = await AddAsync(a, playlistId, friendId, canEdit: false);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        string etag = await ETagAsync(b, playlistId);

        // Act
        using HttpResponseMessage renamed = await PatchAsync(b, playlistId, "Renamed by a reader", etag);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);
    }

    /// <summary>With `canEdit` they may write.</summary>
    [Fact]
    public async Task Patch_AsACollaboratorWithCanEdit_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("editor-owner@example.com");
        (HttpClient editor, Guid editorId) = await SignInAsync("editor-collab@example.com");

        using HttpClient a = owner;
        using HttpClient b = editor;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Editable");
        using HttpResponseMessage added = await AddAsync(a, playlistId, editorId, canEdit: true);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        string etag = await ETagAsync(b, playlistId);

        // Act
        using HttpResponseMessage renamed = await PatchAsync(b, playlistId, "Renamed by an editor", etag);

        // Assert
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    /// <summary>Naming the same person twice is a conflict the database decides.</summary>
    [Fact]
    public async Task AddCollaborator_Twice_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("twice-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("twice-collab@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Once");
        using HttpResponseMessage first = await AddAsync(a, playlistId, friendId, canEdit: false);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using HttpResponseMessage second = await AddAsync(a, playlistId, friendId, canEdit: true);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>The owner cannot be named: they already hold every right a grant could give.</summary>
    [Fact]
    public async Task AddCollaborator_ForTheOwner_Returns409Async()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("self-collab@example.com");

        using HttpClient a = owner;
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Mine");

        // Act
        using HttpResponseMessage response = await AddAsync(a, playlistId, ownerId, canEdit: true);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Only the owner names people.</summary>
    [Fact]
    public async Task AddCollaborator_AsACollaborator_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("grant-owner@example.com");
        (HttpClient editor, Guid editorId) = await SignInAsync("grant-editor@example.com");
        (HttpClient outsider, Guid outsiderId) = await SignInAsync("grant-outsider@example.com");

        using HttpClient a = owner;
        using HttpClient b = editor;
        using HttpClient c = outsider;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Grants");
        using HttpResponseMessage added = await AddAsync(a, playlistId, editorId, canEdit: true);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // Act — an editor tries to name somebody else.
        using HttpResponseMessage response = await AddAsync(b, playlistId, outsiderId, canEdit: false);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Removing a collaborator takes their read access with it.</summary>
    [Fact]
    public async Task RemoveCollaborator_RemovesTheirAccessAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("remove-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("remove-collab@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Temporary");
        using HttpResponseMessage added = await AddAsync(a, playlistId, friendId, canEdit: false);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // Act
        using HttpResponseMessage removed = await a.DeleteAsync(
            $"{PlaylistsPath}/{playlistId}/collaborators/{friendId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using HttpResponseMessage read = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    /// <summary>A stranger cannot list who is named on a private playlist.</summary>
    [Fact]
    public async Task ListCollaborators_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("list-collab-owner@example.com");
        (HttpClient stranger, _) = await SignInAsync("list-collab-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "Private list");

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}/collaborators",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A member sees the list, including each grant's `canEdit`.</summary>
    [Fact]
    public async Task ListCollaborators_AsAMember_ReturnsTheGrantsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("grants-owner@example.com");
        (HttpClient friend, Guid friendId) = await SignInAsync("grants-collab@example.com");

        using HttpClient a = owner;
        using HttpClient b = friend;

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(a, "With grants");
        using HttpResponseMessage added = await AddAsync(a, playlistId, friendId, canEdit: true);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{PlaylistsPath}/{playlistId}/collaborators",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<GrantBody>? grants = await response.Content.ReadFromJsonAsync<List<GrantBody>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(grants);
        GrantBody grant = Assert.Single(grants);
        Assert.Equal(friendId, grant.UserId);
        Assert.True(grant.CanEdit);
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

    private static async Task<HttpResponseMessage> AddAsync(
        HttpClient client,
        Guid playlistId,
        Guid userId,
        bool canEdit) =>
        await client.PostAsJsonAsync(
            $"{PlaylistsPath}/{playlistId}/collaborators",
            new { userId, canEdit },
            TestContext.Current.CancellationToken);

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

        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>One grant, as a client reads it.</summary>
    /// <param name="UserId">The account named.</param>
    /// <param name="CanEdit">Whether they may write.</param>
    internal sealed record GrantBody(Guid UserId, bool CanEdit);
}
