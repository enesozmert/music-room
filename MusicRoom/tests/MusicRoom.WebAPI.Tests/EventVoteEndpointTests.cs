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
/// The vote itself: idempotence, withdrawal, and whose vote it is — SPEC-018, subject V.2.1.
/// </summary>
/// <remarks>
/// A vote is `PUT` because it is an idempotent fact. The test that matters is that a second identical request is
/// `200` and leaves one row: a double submit from a flaky mobile network must be harmless, which is the whole
/// reason the contract chose `PUT` over `POST`.
/// </remarks>
public sealed class EventVoteEndpointTests : IAsyncLifetime
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

    /// <summary>`201` the first time and `200` after, with one row either way.</summary>
    [Fact]
    public async Task Vote_FirstTime_Returns201_ThenAgainReturns200Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await OpenEventAsync("vote-twice@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage first = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        using HttpResponseMessage second = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);

        // Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        IReadOnlyList<EventQueueRequests.QueueBody> queue = await EventQueueRequests.QueueAsync(a, eventId);
        Assert.Equal(1, queue[0].Votes);
    }

    /// <summary>A withdrawn vote stops counting.</summary>
    [Fact]
    public async Task Vote_ThenWithdraw_RemovesItFromTheCountAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await OpenEventAsync("vote-withdraw@example.com");

        using HttpClient a = owner;
        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Act
        using HttpResponseMessage withdrawn = await EventQueueRequests.WithdrawVoteAsync(
            a, eventId, suggestionId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);

        IReadOnlyList<EventQueueRequests.QueueBody> queue = await EventQueueRequests.QueueAsync(a, eventId);
        Assert.Equal(0, queue[0].Votes);
        Assert.False(queue[0].CallerVoted);
    }

    /// <summary>Withdrawing a vote that was never cast is `404`.</summary>
    [Fact]
    public async Task Withdraw_WithoutVoting_Returns404Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await OpenEventAsync("vote-none@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.WithdrawVoteAsync(
            a, eventId, suggestionId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A vote names no account: the voter is the token.</summary>
    [Fact]
    public async Task Vote_IsAttributedToTheTokenAndNotTheRequestAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await OpenEventAsync("vote-attribution@example.com");
        (HttpClient other, _) = await TestBearer.SignInAsync(_factory, _mail, "vote-other@example.com");

        using HttpClient a = owner;
        using HttpClient b = other;

        // Act — the second account votes; the first must not appear to have.
        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(b, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Assert
        IReadOnlyList<EventQueueRequests.QueueBody> mine = await EventQueueRequests.QueueAsync(a, eventId);
        IReadOnlyList<EventQueueRequests.QueueBody> theirs = await EventQueueRequests.QueueAsync(b, eventId);

        Assert.False(mine[0].CallerVoted);
        Assert.True(theirs[0].CallerVoted);
    }

    /// <summary>A suggestion that is not in this event cannot be voted for.</summary>
    [Fact]
    public async Task Vote_ForASuggestionInAnotherEvent_Returns404Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, _) = await OpenEventAsync("vote-cross-event@example.com");

        using HttpClient a = owner;

        Guid otherEventId = await EventRequests.CreateAsync(
            a, "Another event", Visibility.Public, LicenceMode.Everyone);
        Guid elsewhere = await EventQueueRequests.SuggestAsync(a, otherEventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(a, eventId, elsewhere);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A caller the licence does not admit cannot vote.</summary>
    [Fact]
    public async Task Vote_WithoutTheLicence_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "vote-gate-owner@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(_factory, _mail, "vote-gate-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid eventId = await EventRequests.CreateAsync(
            a, "Readable only", Visibility.Public, LicenceMode.InvitedOnly);
        Guid suggestionId = await EventQueueRequests.SuggestAsync(a, eventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(b, eventId, suggestionId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Voting is audited — ADR-0033 on the contended path.</summary>
    [Fact]
    public async Task Vote_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await OpenEventAsync("vote-audited@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage voted = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);
        Assert.Equal(HttpStatusCode.Created, voted.StatusCode);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Action == "event.vote", TestContext.Current.CancellationToken);

        Assert.Equal(eventId, record.TargetId);
        Assert.Equal("completed", record.Outcome);
    }

    /// <summary>An open event with one suggestion in it.</summary>
    private async Task<(HttpClient Owner, Guid EventId, Guid SuggestionId)> OpenEventAsync(string email)
    {
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid eventId = await EventRequests.CreateAsync(
            owner, "Open event", Visibility.Public, LicenceMode.Everyone);
        Guid suggestionId = await EventQueueRequests.SuggestAsync(owner, eventId);

        return (owner, eventId, suggestionId);
    }
}
