using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Hubs;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What a joined client actually receives — SPEC-016, ADR-0035, subject V.2.3's "real-time" half.
/// </summary>
/// <remarks>
/// Written as a real client on a real hub against a real database: the claim under test is that a REST write
/// reaches a listener **after** it has committed, with the version that write produced. A mocked
/// <c>IHubContext</c> would prove that a method was called, which is not the same claim.
/// </remarks>
public sealed class PlaylistBroadcastTests : IAsyncLifetime
{
    /// <summary>How long a test waits for a message before calling it lost.</summary>
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

    /// <summary>An added track reaches a listener, carrying the version the write produced.</summary>
    [Fact]
    public async Task AddTrack_ReachesAJoinedClient_WithTheNewVersionAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-add@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        TaskCompletionSource<TrackEvent> received = Listen<TrackEvent>(listener, PlaylistHub.TrackAddedEvent);

        // Act
        Guid trackId = await TrackRequests.AddAsync(a, playlistId, "dQw4w9WgXcQ");

        // Assert
        TrackEvent message = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(playlistId, message.PlaylistId);
        Assert.Equal(trackId, message.PlaylistTrackId);
        Assert.NotEqual(0u, message.Version);

        // The version in the message is the one the next write must send back as If-Match: one token, two
        // transports — SPEC-016 decision 2.
        Assert.Equal(
            $"\"{message.Version}\"",
            await TrackRequests.ETagAsync(a, playlistId));
    }

    /// <summary>A moved track reaches a listener — the event subject V.2.3 asks for.</summary>
    [Fact]
    public async Task MoveTrack_ReachesAJoinedClientAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-move@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid first = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");
        Guid second = await TrackRequests.AddAsync(a, playlistId, "bbbbbbbbbbb");

        TaskCompletionSource<TrackEvent> received = Listen<TrackEvent>(listener, PlaylistHub.TrackMovedEvent);

        // Act
        using HttpResponseMessage moved = await TrackRequests.PatchTrackAsync(
            a, playlistId, second, new { beforeTrackId = first }, await TrackRequests.ETagAsync(a, playlistId));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // Assert
        TrackEvent message = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(second, message.PlaylistTrackId);
    }

    /// <summary>A removed track reaches a listener.</summary>
    [Fact]
    public async Task RemoveTrack_ReachesAJoinedClientAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-remove@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid trackId = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        TaskCompletionSource<TrackEvent> received = Listen<TrackEvent>(
            listener, PlaylistHub.TrackRemovedEvent);

        // Act
        using HttpResponseMessage removed = await TrackRequests.DeleteTrackAsync(
            a, playlistId, trackId, await TrackRequests.ETagAsync(a, playlistId));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Assert
        TrackEvent message = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(trackId, message.PlaylistTrackId);
    }

    /// <summary>Naming a collaborator reaches a listener — the fourth event of SDD §9.2.</summary>
    [Fact]
    public async Task AddCollaborator_ReachesAJoinedClientAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-collab@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid friendId = await AccountAsync("broadcast-collab-friend@example.com");

        TaskCompletionSource<CollaboratorEvent> received = Listen<CollaboratorEvent>(
            listener, PlaylistHub.CollaboratorChangedEvent);

        // Act
        using HttpResponseMessage added = await a.PostAsJsonAsync(
            $"/api/v1/playlists/{playlistId}/collaborators",
            new { userId = friendId, canEdit = true },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // Assert
        CollaboratorEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(friendId, message.UserId);
        Assert.False(message.Removed);
    }

    /// <summary>
    /// A refused write sends nothing: the queue is drained for an action that happened, and a `412` did not
    /// happen — ADR-0035.
    /// </summary>
    [Fact]
    public async Task AStaleWrite_SendsNothingAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-stale@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        // Listen before the write that succeeds, and consume its message. Attaching the handler
        // afterwards is a race: the successful add's broadcast can arrive once the handler is on, and
        // then it is mistaken for the refused write's. That is how this test failed in CI with
        // "Assert.Throws() Failure: No exception was thrown".
        TaskCompletionSource<TrackEvent> accepted = Listen<TrackEvent>(listener, PlaylistHub.TrackAddedEvent);

        string stale = await TrackRequests.ETagAsync(a, playlistId);
        Guid addedId = await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa", stale);

        TrackEvent first = await accepted.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(addedId, first.PlaylistTrackId);

        TaskCompletionSource<TrackEvent> next = Listen<TrackEvent>(listener, PlaylistHub.TrackAddedEvent);

        // Act — the same token again, which the first add has already invalidated.
        using HttpResponseMessage refused = await TrackRequests.PostTrackAsync(
            a, playlistId, new { youtubeVideoId = "bbbbbbbbbbb" }, stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);

        // Assert by fence, not by waiting. A write that must broadcast goes out after the refused one,
        // and messages reach a connection in the order the server sent them: if the `412` had
        // broadcast, its message arrives first and this assertion names the wrong track. Waiting for a
        // timeout would prove the same thing only probably, and would cost two seconds to do it.
        Guid fenceId = await TrackRequests.AddAsync(a, playlistId, "ccccccccccc");

        TrackEvent seen = await next.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(fenceId, seen.PlaylistTrackId);
    }

    /// <summary>A client that left the group hears nothing more.</summary>
    [Fact]
    public async Task AClientWhoLeft_ReceivesNothingAsync()
    {
        // Arrange
        (HttpClient owner, Guid playlistId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-left@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        await listener.InvokeAsync(
            PlaylistHub.LeaveMethod, playlistId, TestContext.Current.CancellationToken);

        TaskCompletionSource<TrackEvent> received = Listen<TrackEvent>(listener, PlaylistHub.TrackAddedEvent);

        // Act
        await TrackRequests.AddAsync(a, playlistId, "aaaaaaaaaaa");

        // Assert through a round trip, not a wait. A hub invocation's completion travels the same
        // connection as a broadcast and in the same order, so once this returns, any message the
        // server had already sent to this connection has arrived. Leaving twice is safe: the hub
        // method is `Groups.RemoveFromGroupAsync` and nothing else.
        await listener.InvokeAsync(
            PlaylistHub.LeaveMethod, playlistId, TestContext.Current.CancellationToken);

        Assert.False(received.Task.IsCompleted, "a client that left the group received a broadcast");
    }

    /// <summary>An owner with a playlist, already listening to it.</summary>
    private async Task<(HttpClient Owner, Guid PlaylistId, HubConnection Connection)> ListeningOwnerAsync(
        string email)
    {
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(owner, "Live");

        HubConnection connection = HubClients.Build(_factory, pair.AccessToken);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync(
            PlaylistHub.JoinMethod, playlistId, TestContext.Current.CancellationToken);

        return (owner, playlistId, connection);
    }

    /// <summary>A promise of the first message of one event name.</summary>
    private static TaskCompletionSource<TMessage> Listen<TMessage>(
        HubConnection connection,
        string eventName)
    {
        TaskCompletionSource<TMessage> received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<TMessage>(eventName, message => received.TrySetResult(message));

        return received;
    }

    private async Task<Guid> AccountAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);
        client.Dispose();

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A track event, as a client reads it.</summary>
    /// <param name="PlaylistId">The playlist.</param>
    /// <param name="Version">The version after the change.</param>
    /// <param name="PlaylistTrackId">The track.</param>
    internal sealed record TrackEvent(Guid PlaylistId, uint Version, Guid PlaylistTrackId);

    /// <summary>A collaborator event, as a client reads it.</summary>
    /// <param name="PlaylistId">The playlist.</param>
    /// <param name="Version">The version.</param>
    /// <param name="UserId">Who was named or removed.</param>
    /// <param name="Removed">Whether they were taken off.</param>
    internal sealed record CollaboratorEvent(Guid PlaylistId, uint Version, Guid UserId, bool Removed);
}
