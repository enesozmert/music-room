using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Hubs;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What a joined listener receives from `EventHub` — SPEC-019, ADR-0036, subject V.2.1's "live".
/// </summary>
/// <remarks>
/// The claim under test is that a REST write reaches a listener **after** it has committed, carrying the
/// authoritative value rather than a delta: `VoteChanged` says `votes: 2`, not `+1`, so a client that missed a
/// message converges instead of drifting.
/// </remarks>
public sealed class EventBroadcastTests : IAsyncLifetime
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

    /// <summary>A suggestion reaches a listener.</summary>
    [Fact]
    public async Task Suggest_ReachesAJoinedClientAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-suggest@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        TaskCompletionSource<SuggestedEvent> received = Listen<SuggestedEvent>(
            listener, EventHub.TrackSuggestedEvent);

        // Act
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Assert
        SuggestedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(eventId, message.EventId);
        Assert.Equal(suggestionId, message.SuggestionId);
        Assert.Equal(EventQueueRequests.SomeVideoId, message.YoutubeVideoId);
    }

    /// <summary>A vote reaches a listener carrying the new count, not a delta — ADR-0036.</summary>
    [Fact]
    public async Task Vote_ReachesAJoinedClient_WithTheNewCountAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-vote@example.com");
        (HttpClient second, _) = await TestBearer.SignInAsync(_factory, _mail, "broadcast-voter@example.com");

        using HttpClient a = owner;
        using HttpClient b = second;
        await using HubConnection listener = connection;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Registered before either vote, waiting for the count the **second** one produces.
        TaskCompletionSource<VoteEvent> received = ListenFor<VoteEvent>(
            listener, EventHub.VoteChangedEvent, message => message.Votes == 2);

        // Act
        using HttpResponseMessage first = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage second2 = await EventQueueRequests.VoteAsync(b, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, second2.StatusCode);

        // Assert
        VoteEvent message = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(suggestionId, message.SuggestionId);
        Assert.Equal(2, message.Votes);
    }

    /// <summary>A withdrawal reaches a listener with the lowered count.</summary>
    [Fact]
    public async Task Withdraw_ReachesAJoinedClient_WithTheLoweredCountAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-withdraw@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Registered before the vote, waiting for the count the withdrawal produces.
        TaskCompletionSource<VoteEvent> received = ListenFor<VoteEvent>(
            listener, EventHub.VoteChangedEvent, message => message.Votes == 0);

        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Act
        using HttpResponseMessage withdrawn = await EventQueueRequests.WithdrawVoteAsync(
            a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);

        // Assert
        VoteEvent message = await received.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(0, message.Votes);
    }

    /// <summary>A removal reaches a listener as `QueueReordered`.</summary>
    [Fact]
    public async Task RemoveSuggestion_ReachesAJoinedClientAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-remove@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        TaskCompletionSource<ReorderedEvent> received = Listen<ReorderedEvent>(
            listener, EventHub.QueueReorderedEvent);

        // Act
        using HttpResponseMessage removed = await a.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Assert
        ReorderedEvent message = await received.Task.WaitAsync(
            Patience, TestContext.Current.CancellationToken);

        Assert.Equal(suggestionId, message.RemovedSuggestionId);
    }

    /// <summary>A refused write sends nothing: a `422` is not an event.</summary>
    [Fact]
    public async Task ARefusedVote_SendsNothingAsync()
    {
        // Arrange — a restricted event where a geofence is required, so a vote with no proof is `422`.
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "broadcast-refused@example.com");

        using HttpClient a = owner;

        Guid eventId = await RestrictedEventAsync(a);
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        await using HubConnection listener = HubClients.Build(
            _factory, pair.AccessToken, path: EventHub.Path);
        await listener.StartAsync(TestContext.Current.CancellationToken);
        await listener.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken);

        TaskCompletionSource<VoteEvent> received = Listen<VoteEvent>(listener, EventHub.VoteChangedEvent);

        // Act
        using HttpResponseMessage refused = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        // Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    /// <summary>A duplicate vote changed nothing, so nobody is told.</summary>
    [Fact]
    public async Task ADuplicateVote_SendsNothingAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-duplicate@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Every `VoteChanged` from here on, so the assertion is about how many were sent rather than when.
        List<VoteEvent> received = Collect<VoteEvent>(listener, EventHub.VoteChangedEvent);

        using HttpResponseMessage first = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act — the same account votes again, which changes nothing.
        using HttpResponseMessage again = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // Assert — one message for the vote that happened, and none for the one that did not.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        VoteEvent only = Assert.Single(received);
        Assert.Equal(1, only.Votes);
    }

    /// <summary>The group key is the event: a listener on another event hears nothing.</summary>
    [Fact]
    public async Task AClientListeningToAnotherEvent_ReceivesNothingAsync()
    {
        // Arrange
        (HttpClient owner, Guid listenedTo, HubConnection connection) =
            await ListeningOwnerAsync("broadcast-other-event@example.com");

        using HttpClient a = owner;
        await using HubConnection listener = connection;

        Guid elsewhere = await EventRequests.CreateAsync(
            a, "Another event", Visibility.Public, LicenceMode.Everyone);

        TaskCompletionSource<SuggestedEvent> received = Listen<SuggestedEvent>(
            listener, EventHub.TrackSuggestedEvent);

        // Act — a suggestion on the event nobody is listening to.
        await EventQueueRequests.SuggestAsync(a, elsewhere);

        // Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.NotEqual(listenedTo, elsewhere);
    }

    /// <summary>An owner with an open event, already listening to it.</summary>
    private async Task<(HttpClient Owner, Guid EventId, HubConnection Connection)> ListeningOwnerAsync(
        string email)
    {
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid eventId = await EventRequests.CreateAsync(
            owner, "Live event", Visibility.Public, LicenceMode.Everyone);

        HubConnection connection = HubClients.Build(_factory, pair.AccessToken, path: EventHub.Path);
        await connection.StartAsync(TestContext.Current.CancellationToken);
        await connection.InvokeAsync(EventHub.JoinMethod, eventId, TestContext.Current.CancellationToken);

        return (owner, eventId, connection);
    }

    /// <summary>An event where a geofence is required, so a vote with no proof is refused.</summary>
    private static async Task<Guid> RestrictedEventAsync(HttpClient owner)
    {
        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            EventRequests.EventsPath,
            new
            {
                name = "At the venue",
                startsAtUtc = DateTime.UtcNow.AddHours(-1),
                endsAtUtc = DateTime.UtcNow.AddHours(1),
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
                presenceProof = PresenceProof.Geofence,
                geofence = new { latitude = 48.8566m, longitude = 2.3522m, radiusMetres = 250 },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        EventRequests.CreatedEvent? body = await created.Content.ReadFromJsonAsync<EventRequests.CreatedEvent>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body.EventId;
    }

    /// <summary>A promise of the first message of one event name.</summary>
    /// <remarks>
    /// Registered **before** the write it is waiting for. A message from an earlier write travels
    /// asynchronously, so a listener attached afterwards can still receive it — a race in the test, not in the
    /// server, and one that would otherwise make these assertions read the wrong message.
    /// </remarks>
    private static TaskCompletionSource<TMessage> Listen<TMessage>(
        HubConnection connection,
        string eventName) =>
        ListenFor<TMessage>(connection, eventName, _ => true);

    /// <summary>A promise of the first message of one event name that means what the test is waiting for.</summary>
    private static TaskCompletionSource<TMessage> ListenFor<TMessage>(
        HubConnection connection,
        string eventName,
        Func<TMessage, bool> matches)
    {
        TaskCompletionSource<TMessage> received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<TMessage>(
            eventName,
            message =>
            {
                if (matches(message))
                {
                    received.TrySetResult(message);
                }
            });

        return received;
    }

    /// <summary>Counts every message of one event name, from the moment it is registered.</summary>
    private static List<TMessage> Collect<TMessage>(HubConnection connection, string eventName)
    {
        List<TMessage> received = [];

        connection.On<TMessage>(eventName, message => received.Add(message));

        return received;
    }

    /// <summary>`TrackSuggested`, as a client reads it.</summary>
    /// <param name="EventId">The event.</param>
    /// <param name="SuggestionId">The suggestion.</param>
    /// <param name="YoutubeVideoId">The video.</param>
    internal sealed record SuggestedEvent(Guid EventId, Guid SuggestionId, string YoutubeVideoId);

    /// <summary>`VoteChanged`, as a client reads it.</summary>
    /// <param name="EventId">The event.</param>
    /// <param name="SuggestionId">The suggestion.</param>
    /// <param name="Votes">The suggestion's new count.</param>
    internal sealed record VoteEvent(Guid EventId, Guid SuggestionId, int Votes);

    /// <summary>`QueueReordered`, as a client reads it.</summary>
    /// <param name="EventId">The event.</param>
    /// <param name="RemovedSuggestionId">The suggestion that is gone.</param>
    internal sealed record ReorderedEvent(Guid EventId, Guid RemovedSuggestionId);
}
