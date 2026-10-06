using System.Net;
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
/// Suggestions and the vote-ordered queue — SPEC-018, subject V.2.1.
/// </summary>
/// <remarks>
/// The ordering test is the one that matters: the ranking is counted from the vote rows on every read, so a
/// queue that ranks correctly here cannot be a queue with a counter somebody forgot to update.
/// </remarks>
public sealed class EventSuggestionEndpointTests : IAsyncLifetime
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

    /// <summary>A suggestion comes back in the queue.</summary>
    [Fact]
    public async Task Suggest_ThenReadQueue_ReturnsItAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("suggest-owner@example.com");

        using HttpClient a = owner;

        // Act
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Assert
        EventQueueRequests.QueueBody only = Assert.Single(await EventQueueRequests.QueueAsync(a, eventId));
        Assert.Equal(suggestionId, only.SuggestionId);
        Assert.Equal(0, only.Votes);
        Assert.False(only.CallerVoted);
    }

    /// <summary>The same video twice is `409` — two rows would split its votes.</summary>
    [Fact]
    public async Task Suggest_TheSameVideoTwice_Returns409Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("suggest-duplicate@example.com");

        using HttpClient a = owner;
        await EventQueueRequests.SuggestAsync(a, eventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.PostSuggestionAsync(
            a, eventId, EventQueueRequests.SomeVideoId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>An id that is not a YouTube id is `422`.</summary>
    [Fact]
    public async Task Suggest_WithAMalformedVideoId_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("suggest-malformed@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.PostSuggestionAsync(
            a, eventId, "not a video");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["youtubeVideoId"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>A caller the licence does not admit is refused with `404`.</summary>
    [Fact]
    public async Task Suggest_WithoutTheLicence_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "suggest-gate-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(
            _factory, _mail, "suggest-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        // Public, so the stranger can see it, but invited-only, so they may not write to it.
        Guid eventId = await EventRequests.CreateAsync(
            a, "Readable only", Visibility.Public, LicenceMode.InvitedOnly);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.PostSuggestionAsync(
            b, eventId, EventQueueRequests.SomeVideoId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await EventQueueRequests.QueueAsync(a, eventId));
    }

    /// <summary>The suggester may remove their own suggestion.</summary>
    [Fact]
    public async Task RemoveSuggestion_AsTheSuggester_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("remove-owner@example.com");
        (HttpClient guest, _) = await TestBearer.SignInAsync(_factory, _mail, "remove-suggester@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(b, eventId);

        // Act
        using HttpResponseMessage response = await b.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await EventQueueRequests.QueueAsync(a, eventId));
    }

    /// <summary>So may the event's owner.</summary>
    [Fact]
    public async Task RemoveSuggestion_AsTheOwner_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("remove-by-owner@example.com");
        (HttpClient guest, _) = await TestBearer.SignInAsync(_factory, _mail, "remove-guest@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(b, eventId);

        // Act
        using HttpResponseMessage response = await a.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>And nobody else, even another participant.</summary>
    [Fact]
    public async Task RemoveSuggestion_AsAnotherVoter_Returns404Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("remove-other-owner@example.com");
        (HttpClient suggester, _) = await TestBearer.SignInAsync(_factory, _mail, "remove-mine@example.com");
        (HttpClient other, _) = await TestBearer.SignInAsync(_factory, _mail, "remove-not-mine@example.com");

        using HttpClient a = owner;
        using HttpClient b = suggester;
        using HttpClient c = other;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(b, eventId);

        // Act
        using HttpResponseMessage response = await c.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await EventQueueRequests.QueueAsync(a, eventId));
    }

    /// <summary>Removing a suggestion takes its votes with it — no count that counts nothing.</summary>
    [Fact]
    public async Task RemoveSuggestion_TakesItsVotesWithItAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("remove-votes-owner@example.com");

        using HttpClient a = owner;
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Act
        using HttpResponseMessage removed = await a.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Assert.False(
            await context.EventVotes.AnyAsync(
                vote => vote.SuggestionId == suggestionId,
                TestContext.Current.CancellationToken));
    }

    /// <summary>The queue is ordered by vote count, which is counted and never stored.</summary>
    [Fact]
    public async Task Queue_IsOrderedByVoteCountAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("queue-order-owner@example.com");
        (HttpClient second, _) = await TestBearer.SignInAsync(_factory, _mail, "queue-order-voter@example.com");

        using HttpClient a = owner;
        using HttpClient b = second;

        Guid first = await EventQueueRequests.SuggestAsync(a, eventId, "aaaaaaaaaaa");
        Guid favourite = await EventQueueRequests.SuggestAsync(a, eventId, "bbbbbbbbbbb");

        // Act — two accounts vote for the second suggestion, one for the first.
        using HttpResponseMessage one = await EventQueueRequests.VoteAsync(a, eventId, favourite);
        using HttpResponseMessage two = await EventQueueRequests.VoteAsync(b, eventId, favourite);
        using HttpResponseMessage three = await EventQueueRequests.VoteAsync(a, eventId, first);

        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, two.StatusCode);
        Assert.Equal(HttpStatusCode.Created, three.StatusCode);

        // Assert
        IReadOnlyList<EventQueueRequests.QueueBody> queue = await EventQueueRequests.QueueAsync(a, eventId);

        Assert.Equal(favourite, queue[0].SuggestionId);
        Assert.Equal(2, queue[0].Votes);
        Assert.Equal(first, queue[1].SuggestionId);
        Assert.Equal(1, queue[1].Votes);
    }

    /// <summary>Each row says whether the caller voted, so a client can render a toggle.</summary>
    [Fact]
    public async Task Queue_ReportsWhetherTheCallerVotedAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("queue-voted-owner@example.com");
        (HttpClient other, _) = await TestBearer.SignInAsync(_factory, _mail, "queue-not-voted@example.com");

        using HttpClient a = owner;
        using HttpClient b = other;

        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);
        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Act
        IReadOnlyList<EventQueueRequests.QueueBody> mine = await EventQueueRequests.QueueAsync(a, eventId);
        IReadOnlyList<EventQueueRequests.QueueBody> theirs = await EventQueueRequests.QueueAsync(b, eventId);

        // Assert
        Assert.True(mine[0].CallerVoted);
        Assert.False(theirs[0].CallerVoted);
        Assert.Equal(1, theirs[0].Votes);
    }

    /// <summary>An event the caller may not see has no queue to read.</summary>
    [Fact]
    public async Task Queue_AsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "queue-private-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(_factory, _mail, "queue-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid eventId = await EventRequests.CreateAsync(a, "Private", Visibility.Private);

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{EventRequests.EventsPath}/{eventId}/queue",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An event anybody may vote in, inside its window.</summary>
    private async Task<(HttpClient Owner, Guid EventId)> OpenEventAsync(string email)
    {
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid eventId = await EventRequests.CreateAsync(
            owner, "Open event", Visibility.Public, LicenceMode.Everyone);

        return (owner, eventId);
    }
}
