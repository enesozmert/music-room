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
/// The three gates of an event — SPEC-017, subject V.2.1 and V.6.
/// </summary>
/// <remarks>
/// `member` is the owner, an invitee, or anybody when the event is public. `owner` is the owner alone. Every
/// refusal is `404`, never `403`: a distinct answer would confirm that a private event exists.
/// </remarks>
public sealed class EventAccessEndpointTests : IAsyncLifetime
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

    /// <summary>A private event is invisible to a stranger.</summary>
    [Fact]
    public async Task Get_APrivateEventAsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-owner@example.com");
        (HttpClient stranger, _) = await SignInAsync("gate-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid eventId = await EventRequests.CreateAsync(a, "Private", Visibility.Private);

        // Act
        using HttpResponseMessage response = await EventRequests.GetAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An invitation is what makes a private event reachable.</summary>
    [Fact]
    public async Task Get_APrivateEventAsAnInvitee_Returns200Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-inviter@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("gate-invitee@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid eventId = await EventRequests.CreateAsync(a, "Invited", Visibility.Private);

        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // Act
        using HttpResponseMessage response = await EventRequests.GetAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Subject V.2.1: when an event is public, all users can find it.</summary>
    [Fact]
    public async Task Get_APublicEventAsAnyone_Returns200Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-public-owner@example.com");
        (HttpClient anyone, _) = await SignInAsync("gate-public-reader@example.com");

        using HttpClient a = owner;
        using HttpClient b = anyone;

        Guid eventId = await EventRequests.CreateAsync(a, "Public", Visibility.Public, LicenceMode.Everyone);

        // Act
        using HttpResponseMessage response = await EventRequests.GetAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>`owner` is not `member`: an invitee may read and not edit.</summary>
    [Fact]
    public async Task Patch_AsANonOwner_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-edit-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("gate-edit-invitee@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid eventId = await EventRequests.CreateAsync(a, "Not yours", Visibility.Private);
        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // The invitee can read it, so they hold a real token — which still buys them nothing.
        string etag = await EventRequests.ETagAsync(b, eventId);

        // Act
        using HttpResponseMessage response = await EventRequests.PatchAsync(b, eventId, "Mine now", etag);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A withdrawn invitation takes effect on the next call, not on the next sign-in.</summary>
    [Fact]
    public async Task Uninvite_RemovesTheInviteesAccessAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-uninvite-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("gate-uninvited@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid eventId = await EventRequests.CreateAsync(a, "Briefly invited", Visibility.Private);
        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        using HttpResponseMessage before = await EventRequests.GetAsync(b, eventId);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Act
        using HttpResponseMessage withdrawn = await a.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/invitations/{guestId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);

        using HttpResponseMessage after = await EventRequests.GetAsync(b, eventId);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    /// <summary>Invitations are the owner's: an invitee cannot invite anybody else.</summary>
    [Fact]
    public async Task Invite_AsANonOwner_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-invite-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("gate-invite-guest@example.com");
        (HttpClient outsider, Guid outsiderId) = await SignInAsync("gate-invite-outsider@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;
        outsider.Dispose();

        Guid eventId = await EventRequests.CreateAsync(a, "Owner's list", Visibility.Private);
        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // Act — the invitee tries to bring a friend.
        using HttpResponseMessage response = await EventRequests.InviteAsync(b, eventId, outsiderId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Who was invited is the owner's business alone.</summary>
    [Fact]
    public async Task ListInvitations_AsAnInvitee_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("gate-list-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("gate-list-invitee@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        Guid eventId = await EventRequests.CreateAsync(a, "Guest list", Visibility.Private);
        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // Act
        using HttpResponseMessage response = await b.GetAsync(
            $"{EventRequests.EventsPath}/{eventId}/invitations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
}
