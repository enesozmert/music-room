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
/// Twenty people voting at once — AGENTS.md's parallel rule, on the write subject V.2.1 grades.
/// </summary>
/// <remarks>
/// Two races, and they fail in opposite directions. Twenty accounts voting for one track must produce **twenty**
/// votes: a counter incremented in application code loses some of them, which is the lost-update bug the subject
/// asks about, and it is absent here because nothing is incremented — the count is read from the rows. Twenty
/// parallel requests from **one** account must produce **one** vote: that is the unique index arbitrating a
/// duplicate, not a read-then-write check.
/// </remarks>
public sealed class EventVoteConcurrencyTests : IAsyncLifetime
{
    /// <summary>How many clients race.</summary>
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
    /// Twenty accounts vote for one suggestion in parallel: twenty rows, and a count of twenty.
    /// </summary>
    [Fact]
    public async Task Vote_TwentyAccountsOnOneSuggestion_CountsTwentyAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("race-owner@example.com");

        using HttpClient a = owner;
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        List<HttpClient> voters = [];

        for (int index = 0; index < Racers; index++)
        {
            (HttpClient voter, _) = await TestBearer.SignInAsync(
                _factory, _mail, $"race-voter-{index}@example.com");
            voters.Add(voter);
        }

        try
        {
            // Act
            HttpStatusCode[] answers = await Task.WhenAll(
                voters.Select(async voter =>
                {
                    using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
                        voter, eventId, suggestionId);

                    return response.StatusCode;
                }));

            // Assert — every one of them was recorded. A counter in application code would have lost some.
            Assert.All(answers, status => Assert.Equal(HttpStatusCode.Created, status));

            IReadOnlyList<EventQueueRequests.QueueBody> queue =
                await EventQueueRequests.QueueAsync(a, eventId);

            Assert.Equal(Racers, queue[0].Votes);
            Assert.Equal(Racers, await VoteRowsAsync(suggestionId));
        }
        finally
        {
            foreach (HttpClient voter in voters)
            {
                voter.Dispose();
            }
        }
    }

    /// <summary>
    /// Twenty parallel requests from one account write one row: the unique index arbitrates, and a vote stays
    /// idempotent under concurrency.
    /// </summary>
    [Fact]
    public async Task Vote_TwentyParallelVotesFromOneAccount_WriteOneRowAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("race-single-owner@example.com");

        using HttpClient a = owner;
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Act
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async _ =>
            {
                using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
                    a, eventId, suggestionId);

                return response.StatusCode;
            }));

        // Assert — one `201`, the rest `200`, and one row in the database either way.
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(Racers - 1, answers.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, await VoteRowsAsync(suggestionId));

        IReadOnlyList<EventQueueRequests.QueueBody> queue = await EventQueueRequests.QueueAsync(a, eventId);
        Assert.Equal(1, queue[0].Votes);
    }

    /// <summary>
    /// Twenty parallel first-time suggestions of the same video produce one suggestion and nineteen `409`s.
    /// </summary>
    [Fact]
    public async Task Suggest_TwentyParallelSuggestionsOfOneVideo_AddOneAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId) = await OpenEventAsync("race-suggest-owner@example.com");

        using HttpClient a = owner;

        // Act
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async _ =>
            {
                using HttpResponseMessage response = await EventQueueRequests.PostSuggestionAsync(
                    a, eventId, EventQueueRequests.SomeVideoId);

                return response.StatusCode;
            }));

        // Assert
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(Racers - 1, answers.Count(status => status == HttpStatusCode.Conflict));
        Assert.Single(await EventQueueRequests.QueueAsync(a, eventId));
    }

    /// <summary>
    /// Twenty parallel votes carrying one event code spend it once: the redemption index is the arbiter.
    /// </summary>
    [Fact]
    public async Task Vote_TwentyParallelVotesWithOneCode_SpendItOnceAsync()
    {
        // Arrange — a venue event, open now, where a code is the only proof.
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "race-code-owner@example.com");

        using HttpClient a = owner;

        Guid eventId = await RestrictedEventAsync(a);
        Guid first = await EventQueueRequests.SuggestAsync(a, eventId, "aaaaaaaaaaa");
        Guid second = await EventQueueRequests.SuggestAsync(a, eventId, "bbbbbbbbbbb");

        string code = await EventQueueRequests.IssueCodeAsync(a, eventId);

        // Act — the same account, the same code, two suggestions, all at once.
        HttpStatusCode[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(async index =>
            {
                using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
                    a,
                    eventId,
                    index % 2 == 0 ? first : second,
                    new { eventCode = code });

                return response.StatusCode;
            }));

        // Assert — the code admitted this account once, so exactly one vote stands and the rest are refused.
        Assert.Equal(1, answers.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(
            Racers - 1,
            answers.Count(status => status == HttpStatusCode.UnprocessableEntity));

        Assert.Equal(1, await VoteRowsAsync(first) + await VoteRowsAsync(second));
    }

    private async Task<(HttpClient Owner, Guid EventId)> OpenEventAsync(string email)
    {
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid eventId = await EventRequests.CreateAsync(
            owner, "Contested event", Visibility.Public, LicenceMode.Everyone);

        return (owner, eventId);
    }

    private async Task<Guid> RestrictedEventAsync(HttpClient owner)
    {
        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            EventRequests.EventsPath,
            new
            {
                name = "Code at the door",
                startsAtUtc = DateTime.UtcNow.AddHours(-1),
                endsAtUtc = DateTime.UtcNow.AddHours(1),
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
                presenceProof = PresenceProof.EventCode,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        EventRequests.CreatedEvent? body = await created.Content.ReadFromJsonAsync<EventRequests.CreatedEvent>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body.EventId;
    }

    private async Task<int> VoteRowsAsync(Guid suggestionId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.EventVotes
            .AsNoTracking()
            .CountAsync(vote => vote.SuggestionId == suggestionId, TestContext.Current.CancellationToken);
    }
}
