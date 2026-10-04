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
/// Invitations and attendance — SPEC-017 decision 5, subject V.2.1.
/// </summary>
/// <remarks>
/// The claim under test is that attendance is a **roster and not a gate**: on a public event any authenticated
/// caller may attend, and nothing anywhere requires an attendance row first. Subject V.2.1 says all users can
/// find and vote on a public event; a check-in requirement would quietly narrow that.
/// </remarks>
public sealed class EventRosterEndpointTests : IAsyncLifetime
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

    /// <summary>The same person cannot be invited twice — the unique index says so.</summary>
    [Fact]
    public async Task Invite_ADuplicate_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("roster-guest@example.com");

        using HttpClient a = owner;
        guest.Dispose();

        Guid eventId = await EventRequests.CreateAsync(a, "One invitation each");

        using HttpResponseMessage first = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using HttpResponseMessage second = await EventRequests.InviteAsync(a, eventId, guestId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Inviting the owner is a conflict: they already hold the strongest grant.</summary>
    [Fact]
    public async Task Invite_TheOwner_Returns409Async()
    {
        // Arrange
        (HttpClient owner, Guid ownerId) = await SignInAsync("roster-self-invite@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Self invitation");

        // Act
        using HttpResponseMessage response = await EventRequests.InviteAsync(a, eventId, ownerId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>An account that does not exist cannot be invited.</summary>
    [Fact]
    public async Task Invite_AnUnknownAccount_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-unknown-invite@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Nobody");

        // Act
        using HttpResponseMessage response = await EventRequests.InviteAsync(a, eventId, Guid.NewGuid());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The owner can read back who was invited.</summary>
    [Fact]
    public async Task ListInvitations_AsTheOwner_ReturnsThemAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-list-owner@example.com");
        (HttpClient guest, Guid guestId) = await SignInAsync("roster-listed@example.com");

        using HttpClient a = owner;
        guest.Dispose();

        Guid eventId = await EventRequests.CreateAsync(a, "With a guest list");
        using HttpResponseMessage invited = await EventRequests.InviteAsync(a, eventId, guestId);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        // Act
        using HttpResponseMessage response = await a.GetAsync(
            $"{EventRequests.EventsPath}/{eventId}/invitations",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<InvitationBody>? invitations = await response.Content.ReadFromJsonAsync<List<InvitationBody>>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(invitations);
        InvitationBody only = Assert.Single(invitations);
        Assert.Equal(guestId, only.UserId);
    }

    /// <summary>
    /// Any authenticated caller may attend a public event without an invitation — decision 5, and subject
    /// V.2.1's "all users".
    /// </summary>
    [Fact]
    public async Task Attend_APublicEventWithoutAnInvitation_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-public-owner@example.com");
        (HttpClient anyone, _) = await SignInAsync("roster-public-attendee@example.com");

        using HttpClient a = owner;
        using HttpClient b = anyone;

        Guid eventId = await EventRequests.CreateAsync(
            a, "Open to all", Visibility.Public, LicenceMode.Everyone);

        // Act
        using HttpResponseMessage response = await EventRequests.AttendAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>On a private event the invitation is the gate.</summary>
    [Fact]
    public async Task Attend_APrivateEventAsAStranger_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-private-owner@example.com");
        (HttpClient stranger, _) = await SignInAsync("roster-private-stranger@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid eventId = await EventRequests.CreateAsync(a, "Invitation only", Visibility.Private);

        // Act
        using HttpResponseMessage response = await EventRequests.AttendAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>One attendance per person.</summary>
    [Fact]
    public async Task Attend_Twice_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-twice-owner@example.com");
        (HttpClient attendee, _) = await SignInAsync("roster-twice@example.com");

        using HttpClient a = owner;
        using HttpClient b = attendee;

        Guid eventId = await EventRequests.CreateAsync(
            a, "Once each", Visibility.Public, LicenceMode.Everyone);

        using HttpResponseMessage first = await EventRequests.AttendAsync(b, eventId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act
        using HttpResponseMessage second = await EventRequests.AttendAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>Leaving is not a ban: the same person may come back.</summary>
    [Fact]
    public async Task LeaveAttendance_ThenAttendAgain_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-return-owner@example.com");
        (HttpClient attendee, _) = await SignInAsync("roster-returner@example.com");

        using HttpClient a = owner;
        using HttpClient b = attendee;

        Guid eventId = await EventRequests.CreateAsync(
            a, "Come and go", Visibility.Public, LicenceMode.Everyone);

        using HttpResponseMessage joined = await EventRequests.AttendAsync(b, eventId);
        Assert.Equal(HttpStatusCode.Created, joined.StatusCode);

        // Act
        using HttpResponseMessage left = await b.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/attendances/current",
            TestContext.Current.CancellationToken);

        using HttpResponseMessage again = await EventRequests.AttendAsync(b, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    /// <summary>Leaving something the caller never joined is `404`.</summary>
    [Fact]
    public async Task LeaveAttendance_WithoutAttending_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await SignInAsync("roster-never-joined-owner@example.com");
        (HttpClient attendee, _) = await SignInAsync("roster-never-joined@example.com");

        using HttpClient a = owner;
        using HttpClient b = attendee;

        Guid eventId = await EventRequests.CreateAsync(
            a, "Never joined", Visibility.Public, LicenceMode.Everyone);

        // Act
        using HttpResponseMessage response = await b.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/attendances/current",
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

    /// <summary>One invitation, as a client reads it.</summary>
    /// <param name="UserId">Who was invited.</param>
    /// <param name="DisplayName">Their display name.</param>
    private sealed record InvitationBody(Guid UserId, string DisplayName);
}
