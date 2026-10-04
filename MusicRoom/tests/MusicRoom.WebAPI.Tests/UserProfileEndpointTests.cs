using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using MusicRoom.WebAPI.Endpoints;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Reading another account, projected per field — SPEC-013, subject V.1's three tiers and V.6.
/// </summary>
/// <remarks>
/// The tier that matters most here is `Friends`, because it is the one that changes answer as the graph changes:
/// two of these tests befriend and unfriend and read again.
/// </remarks>
public sealed class UserProfileEndpointTests : IAsyncLifetime
{
    internal const string UsersPath = "/api/v1/users";

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

    /// <summary>The owner sees every tier, including private.</summary>
    [Fact]
    public async Task GetUser_AsSelf_ReturnsEveryFieldAsync()
    {
        // Arrange
        (HttpClient client, Guid ownId) = await FilledProfileAsync("self-read@example.com");

        using HttpClient authenticated = client;

        // Act
        ProfileView? view = await ReadAsync(authenticated, ownId);

        // Assert
        Assert.NotNull(view);
        Assert.Equal("Public Bio", view.Bio);
        Assert.Equal("Friendly Name", view.RealName);
        Assert.Equal(new DateOnly(1990, 1, 1), view.BirthDate);
    }

    /// <summary>A stranger sees the public field and nothing else.</summary>
    [Fact]
    public async Task GetUser_AsStranger_ReturnsOnlyPublicFieldsAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await FilledProfileAsync("stranger-target@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "stranger-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        // Act
        ProfileView? view = await ReadAsync(b, ownerId);

        // Assert
        Assert.NotNull(view);
        Assert.Equal("Public Bio", view.Bio);
        Assert.Null(view.RealName);
        Assert.Null(view.Location);
        Assert.Null(view.BirthDate);
        Assert.Equal("Player One", view.DisplayName);
    }

    /// <summary>
    /// A friend sees the friends-only fields and still not the private one, and the graph is what decides:
    /// the same reader saw less a moment earlier.
    /// </summary>
    [Fact]
    public async Task GetUser_AfterBefriending_RevealsTheFriendsFieldsAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await FilledProfileAsync("friend-target@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "friend-reader@example.com");
        Guid readerId = await UserIdAsync("friend-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        ProfileView? asStranger = await ReadAsync(b, ownerId);
        Assert.NotNull(asStranger);
        Assert.Null(asStranger.RealName);

        // Act — befriend, then read again.
        await BefriendAsync(b, a, ownerId, readerId);

        ProfileView? asFriend = await ReadAsync(b, ownerId);

        // Assert
        Assert.NotNull(asFriend);
        Assert.Equal("Friendly Name", asFriend.RealName);
        Assert.Equal("Somewhere", asFriend.Location);

        // Private stays private, friend or not.
        Assert.Null(asFriend.BirthDate);
    }

    /// <summary>Removing the friendship hides the fields again — it is a live check, not a snapshot.</summary>
    [Fact]
    public async Task GetUser_AfterRemovingAFriend_HidesThemAgainAsync()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await FilledProfileAsync("unfriend-target@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "unfriend-reader@example.com");
        Guid readerId = await UserIdAsync("unfriend-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        await BefriendAsync(b, a, ownerId, readerId);
        ProfileView? asFriend = await ReadAsync(b, ownerId);
        Assert.NotNull(asFriend);
        Assert.Equal("Friendly Name", asFriend.RealName);

        // Act
        using HttpResponseMessage removed = await b.DeleteAsync(
            $"/api/v1/me/friends/{ownerId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        // Assert
        ProfileView? afterwards = await ReadAsync(b, ownerId);
        Assert.NotNull(afterwards);
        Assert.Null(afterwards.RealName);
    }

    /// <summary>An unknown id answers like a fully hidden profile — existence is not confirmed.</summary>
    [Fact]
    public async Task GetUser_ForAnUnknownId_Returns200WithNoFieldsAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "asks-unknown@example.com");

        using HttpClient authenticated = client;

        // Act
        ProfileView? view = await ReadAsync(authenticated, Guid.NewGuid());

        // Assert
        Assert.NotNull(view);
        Assert.Empty(view.DisplayName);
        Assert.Null(view.RealName);
        Assert.Null(view.Bio);
    }

    /// <summary>Both routes are `auth`.</summary>
    [Fact]
    public async Task GetUser_WithoutAToken_Returns401Async()
    {
        // Arrange
        using HttpClient anonymous = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await anonymous.GetAsync(
            $"{UsersPath}/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The body carries no address and no hash — §13.5.</summary>
    [Fact]
    public async Task GetUser_ReturnsNoEmailOrHashAsync()
    {
        // Arrange
        const string email = "no-leak-read@example.com";
        (HttpClient owner, Guid ownerId) = await FilledProfileAsync(email);

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await a.GetAsync(
            $"{UsersPath}/{ownerId}",
            TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LoginEndpointTests.AccountPassword, body, StringComparison.Ordinal);
    }

    /// <summary>Search finds a display-name prefix.</summary>
    [Fact]
    public async Task SearchUsers_MatchesADisplayNamePrefixAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "searcher@example.com");

        using HttpClient authenticated = client;

        // Act — every account registers as "Player One", so the prefix matches.
        List<SearchRow> results = await SearchAsync(authenticated, "Play");

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, row => Assert.StartsWith("Play", row.DisplayName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Search does not match a profile field, so a stranger cannot confirm a friends-only value by probing for
    /// it — SPEC-013 decision 3.
    /// </summary>
    [Fact]
    public async Task SearchUsers_DoesNotMatchAProfileFieldAsync()
    {
        // Arrange
        (HttpClient owner, _) = await FilledProfileAsync("hidden-field@example.com");
        (HttpClient reader, _) = await TestBearer.SignInAsync(_factory, _mail, "probing-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = reader;

        // Act — the exact friends-only value.
        List<SearchRow> results = await SearchAsync(b, "Friendly Name");

        // Assert
        Assert.Empty(results);
    }

    /// <summary>A blank query returns nothing rather than everybody.</summary>
    [Fact]
    public async Task SearchUsers_WithABlankQuery_ReturnsNothingAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "blank-search@example.com");

        using HttpClient authenticated = client;

        // Act
        List<SearchRow> results = await SearchAsync(authenticated, "   ");

        // Assert
        Assert.Empty(results);
    }

    /// <summary>An oversized page is capped — OWASP API4.</summary>
    [Fact]
    public async Task SearchUsers_CapsThePageSizeAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "big-page@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.GetAsync(
            $"{UsersPath}?q=Play&pageSize=10000",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<SearchRow>? rows = await response.Content.ReadFromJsonAsync<List<SearchRow>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(rows);
        Assert.True(rows.Count <= UserEndpoints.MaximumPageSize);
    }

    private async Task<(HttpClient Client, Guid UserId)> FilledProfileAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        using HttpResponseMessage updated = await client.PatchAsJsonAsync(
            "/api/v1/me/profile",
            new
            {
                realName = new { value = "Friendly Name", visibility = ProfileFieldVisibility.Friends },
                bio = new { value = "Public Bio", visibility = ProfileFieldVisibility.Public },
                location = new { value = "Somewhere", visibility = ProfileFieldVisibility.Friends },
                birthDate = new { value = "1990-01-01", visibility = ProfileFieldVisibility.Private },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        return (client, await UserIdAsync(email));
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Makes two accounts friends through the API, so the graph is exercised rather than seeded.</summary>
    private static async Task BefriendAsync(
        HttpClient sender,
        HttpClient recipient,
        Guid recipientId,
        Guid senderId)
    {
        using HttpResponseMessage sent = await sender.PostAsJsonAsync(
            "/api/v1/me/friend-requests",
            new { targetUserId = recipientId },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        using HttpResponseMessage listed = await recipient.GetAsync(
            "/api/v1/me/friend-requests?direction=incoming",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        List<RequestRow>? requests = await listed.Content.ReadFromJsonAsync<List<RequestRow>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(requests);

        Guid requestId = requests.Single(request => request.UserId == senderId).RequestId;

        using HttpResponseMessage accepted = await recipient.PatchAsJsonAsync(
            $"/api/v1/me/friend-requests/{requestId}",
            new { status = "accepted" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    private static async Task<ProfileView?> ReadAsync(HttpClient client, Guid userId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{UsersPath}/{userId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ProfileView>(TestContext.Current.CancellationToken);
    }

    private static async Task<List<SearchRow>> SearchAsync(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{UsersPath}?q={Uri.EscapeDataString(query)}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<SearchRow>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>A projected profile, as a client reads it.</summary>
    /// <param name="DisplayName">Always present.</param>
    /// <param name="RealName">Present only when the reader clears its tier.</param>
    /// <param name="Bio">Same.</param>
    /// <param name="Location">Same.</param>
    /// <param name="BirthDate">Same.</param>
    internal sealed record ProfileView(
        string DisplayName,
        string? RealName,
        string? Bio,
        string? Location,
        DateOnly? BirthDate);

    /// <summary>One search row.</summary>
    /// <param name="UserId">The account id.</param>
    /// <param name="DisplayName">The handle.</param>
    internal sealed record SearchRow(Guid UserId, string DisplayName);

    /// <summary>One pending request, as this suite reads it.</summary>
    /// <param name="RequestId">The request id.</param>
    /// <param name="UserId">The other party.</param>
    internal sealed record RequestRow(Guid RequestId, Guid UserId);
}
