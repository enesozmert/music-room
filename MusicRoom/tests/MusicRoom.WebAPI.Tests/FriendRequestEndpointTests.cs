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
/// Friend requests end to end — sending, declining, withdrawing and listing. SPEC-012.
/// </summary>
public sealed class FriendRequestEndpointTests : IAsyncLifetime
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

    /// <summary>The flow starts.</summary>
    [Fact]
    public async Task SendRequest_ToAnotherUser_Returns201Async()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("alice-sends@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("bob-receives@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        // Act
        using HttpResponseMessage response = await SendRequestAsync(a, bobId);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>A request with no target is `422` keyed on `TargetUserId` — SPEC-036.</summary>
    /// <remarks>
    /// The other `422` in this suite is a query-parse refusal on the list route, which answers no `errors`
    /// dictionary. This is the send route's own validator.
    /// </remarks>
    [Fact]
    public async Task SendRequest_WithNoTarget_Returns422KeyedOnTargetUserIdAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("alice-no-target@example.com");

        using HttpClient a = alice;

        // Act
        using HttpResponseMessage response = await SendRequestAsync(a, Guid.Empty);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["targetUserId"], await ValidationProblems.KeysAsync(response));
    }
    /// <summary>A user cannot befriend themselves.</summary>
    [Fact]
    public async Task SendRequest_ToSelf_Returns409Async()
    {
        // Arrange
        (HttpClient client, Guid ownId) = await SignInAsync("self-friend@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await SendRequestAsync(authenticated, ownId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    /// <summary>Asking twice is a conflict, not a second row.</summary>
    [Fact]
    public async Task SendRequest_Twice_Returns409Async()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("asks-twice@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("asked-twice@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage first = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using HttpResponseMessage second = await SendRequestAsync(a, bobId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
    /// <summary>A crossing request is a conflict, not an auto-accept — decision 2.</summary>
    [Fact]
    public async Task SendRequest_WhenTheOtherDirectionIsPending_Returns409Async()
    {
        // Arrange
        (HttpClient alice, Guid aliceId) = await SignInAsync("crossing-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("crossing-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage first = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using HttpResponseMessage crossing = await SendRequestAsync(b, aliceId);

        // Assert — and nobody became a friend of anybody.
        Assert.Equal(HttpStatusCode.Conflict, crossing.StatusCode);
        Assert.Empty(await ListFriendsAsync(a));
        Assert.Empty(await ListFriendsAsync(b));
    }
    /// <summary>An unknown account is a `404`, deliberately distinct from a conflict.</summary>
    [Fact]
    public async Task SendRequest_ToAnUnknownUser_Returns404Async()
    {
        // Arrange
        (HttpClient client, _) = await SignInAsync("asks-nobody@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await SendRequestAsync(authenticated, Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    /// <summary>Declining is not accepting.</summary>
    [Fact]
    public async Task Decline_LeavesNoFriendshipAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("declines-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("declines-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);

        // Act
        using HttpResponseMessage declined = await RespondAsync(b, requestId, "declined");

        // Assert
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        Assert.Empty(await ListFriendsAsync(a));
        Assert.Empty(await ListFriendsAsync(b));
    }
    /// <summary>Only the sender withdraws; a recipient declines instead.</summary>
    [Fact]
    public async Task Cancel_BySenderSucceeds_ByRecipientIs404Async()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("cancels-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("cancels-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        Guid requestId = await IncomingRequestIdAsync(b);

        // Act
        using HttpResponseMessage byRecipient = await b.DeleteAsync(
            $"{RequestsPath}/{requestId}",
            TestContext.Current.CancellationToken);
        using HttpResponseMessage bySender = await a.DeleteAsync(
            $"{RequestsPath}/{requestId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, byRecipient.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, bySender.StatusCode);
    }
    /// <summary>The direction filter means something: a request is in exactly one of the two lists.</summary>
    [Fact]
    public async Task ListRequests_IncomingAndOutgoing_AreDisjointAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("directions-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("directions-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        // Act
        List<RequestRow> aliceOutgoing = await ListRequestsAsync(a, "outgoing");
        List<RequestRow> aliceIncoming = await ListRequestsAsync(a, "incoming");
        List<RequestRow> bobIncoming = await ListRequestsAsync(b, "incoming");

        // Assert
        Assert.Single(aliceOutgoing);
        Assert.Empty(aliceIncoming);
        Assert.Single(bobIncoming);
    }
    /// <summary>An unknown direction is refused rather than silently treated as incoming.</summary>
    [Fact]
    public async Task ListRequests_WithAnUnknownDirection_Returns422Async()
    {
        // Arrange
        (HttpClient client, _) = await SignInAsync("bad-direction@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.GetAsync(
            $"{RequestsPath}?direction=sideways",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
    /// <summary>Sending a request is audited, named and attributed — ADR-0033.</summary>
    [Fact]
    public async Task SendRequest_WritesAnAuditRecordNamingTheCallerAsync()
    {
        // Arrange
        (HttpClient alice, _) = await SignInAsync("audited-friend-a@example.com");
        (HttpClient bob, Guid bobId) = await SignInAsync("audited-friend-b@example.com");

        using HttpClient a = alice;
        using HttpClient b = bob;

        // Act
        using HttpResponseMessage sent = await SendRequestAsync(a, bobId);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "friends.request",
                TestContext.Current.CancellationToken);

        Assert.NotNull(record.UserId);
        Assert.Equal(bobId, record.TargetId);
        Assert.Equal("friend-request", record.TargetType);
        Assert.Equal("completed", record.Outcome);
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
