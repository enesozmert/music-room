using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Friendships end to end — acceptance, symmetry, removal and the parallel accept. SPEC-012.
/// </summary>
public sealed class FriendshipEndpointTests : IAsyncLifetime
{
    internal const string FriendsPath = "/api/v1/me/friends";
    internal const string RequestsPath = "/api/v1/me/friend-requests";

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

    /// <summary>Acceptance is symmetric: both sides see it, and neither can see it alone.</summary>
    [Fact]
    public async Task Accept_MakesTheFriendshipVisibleToBothSidesAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("accepts-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("accepts-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);

        // Act
        using HttpResponseMessage accepted = await RespondAsync(b, requestId, "accepted");

        // Assert
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Single(await ListFriendsAsync(a));
        Assert.Single(await ListFriendsAsync(b));
    }
    /// <summary>
    /// The concurrency case AGENTS.md §5 requires: twenty accepts of one request, in parallel, and the
    /// assertion is on final database state — one friendship row, not twenty.
    /// </summary>
    [Fact]
    public async Task Accept_TwentyParallelAccepts_ProduceExactlyOneFriendshipAsync()
    {
        // Arrange
        const int callers = 20;
        (HttpClient alice, _) = await SignInAsync("contended-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("contended-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => RespondAsync(b, requestId, "accepted")));

        try
        {
            // Assert
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(callers - 1, responses.Count(response => response.StatusCode == HttpStatusCode.NotFound));

            using IServiceScope scope = _factory.Services.CreateScope();
            MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

            Assert.Equal(1, await context.Friendships.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }
    /// <summary>Another account's request answers `404`, never `403`.</summary>
    [Fact]
    public async Task Accept_ARequestThatIsNotMine_Returns404Async()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("not-mine-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("not-mine-b@example.com");
        (HttpClient carol, _) = await SignInAsync("not-mine-c@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;
        using HttpClient c = carol;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);

        // Act — Carol answers a request addressed to Bob.
        using HttpResponseMessage response = await RespondAsync(c, requestId, "accepted");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await ListFriendsAsync(a));
    }
    /// <summary>Removing ends it for both sides, and the pair may try again afterwards — decision 4.</summary>
    [Fact]
    public async Task RemoveFriend_RemovesItForBothSidesAndAllowsARetryAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("removes-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("removes-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);
        using HttpResponseMessage accepted = await RespondAsync(b, requestId, "accepted");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // Act
        using HttpResponseMessage removed = await a.DeleteAsync(
            $"{FriendsPath}/{bobId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await ListFriendsAsync(a));
        Assert.Empty(await ListFriendsAsync(b));

        // And no tombstone blocks a second attempt.
        using HttpResponseMessage again = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }
    /// <summary>Removing somebody who is not a friend is `404`.</summary>
    [Fact]
    public async Task RemoveFriend_WhoIsNotAFriend_Returns404Async()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("strangers-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("strangers-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        // Act
        using HttpResponseMessage response = await a.DeleteAsync(
            $"{FriendsPath}/{bobId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    /// <summary>The `self` gate: one account never sees another's friends.</summary>
    [Fact]
    public async Task ListFriends_DoesNotReturnAnotherAccountsFriendsAsync()
    {
        // Arrange — Alice and Bob are friends; Carol is nobody's friend.
        (HttpClient alice, _) = await SignInAsync("isolated-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("isolated-b@example.com");
        (HttpClient carol, _) = await SignInAsync("isolated-c@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;
        using HttpClient c = carol;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Guid requestId = await IncomingRequestIdAsync(b);
        using HttpResponseMessage accepted = await RespondAsync(b, requestId, "accepted");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // Act and assert
        Assert.Single(await ListFriendsAsync(a));
        Assert.Empty(await ListFriendsAsync(c));
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

    private static async Task<HttpResponseMessage> SendRequestAsync(HttpClient client, Guid targetUserId) =>
        await client.PostAsJsonAsync(
            RequestsPath,
            new { targetUserId },
            TestContext.Current.CancellationToken);

    private static async Task<HttpResponseMessage> RespondAsync(
        HttpClient client,
        Guid requestId,
        string status) =>
        await client.PatchAsJsonAsync(
            $"{RequestsPath}/{requestId}",
            new { status },
            TestContext.Current.CancellationToken);

    private static async Task<Guid> IncomingRequestIdAsync(HttpClient client)
    {
        List<RequestRow> incoming = await ListRequestsAsync(client, "incoming");

        return Assert.Single(incoming).RequestId;
    }

    private static async Task<List<RequestRow>> ListRequestsAsync(HttpClient client, string direction)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{RequestsPath}?direction={direction}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<RequestRow>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    private static async Task<List<FriendRow>> ListFriendsAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            FriendsPath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<FriendRow>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>One friend, as a client reads them.</summary>
    /// <param name="UserId">Their account id.</param>
    /// <param name="DisplayName">Their handle.</param>
    internal sealed record FriendRow(Guid UserId, string DisplayName);

    /// <summary>One pending request, as a client reads it.</summary>
    /// <param name="RequestId">The id the respond and cancel routes take.</param>
    /// <param name="UserId">The other party.</param>
    internal sealed record RequestRow(Guid RequestId, Guid UserId);
}
