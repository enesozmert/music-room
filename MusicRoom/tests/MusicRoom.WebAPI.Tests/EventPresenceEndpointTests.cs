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
/// The location-and-time licence, enforced — ADR-0014, SPEC-018 decisions 5 and 6.
/// </summary>
/// <remarks>
/// This is the suite that makes subject V.2.1's third licence mode real rather than stored. Every refusal is
/// `422` with one message: naming which part of the proof failed would tell a caller whether a code exists, or
/// how far outside the geofence they are.
/// </remarks>
public sealed class EventPresenceEndpointTests : IAsyncLifetime
{
    /// <summary>The venue: a point in Paris, with a 250 m radius.</summary>
    internal const decimal VenueLatitude = 48.8566m;

    /// <summary>The venue's longitude.</summary>
    internal const decimal VenueLongitude = 2.3522m;

    /// <summary>How close counts as present.</summary>
    internal const int VenueRadiusMetres = 250;

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

    /// <summary>No proof at all is refused — the licence is not decorative.</summary>
    [Fact]
    public async Task Vote_OnARestrictedEventWithNoProof_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-none@example.com", PresenceProof.Geofence);

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(a, eventId, suggestionId);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>A coordinate outside the radius is refused — the check is server-side.</summary>
    [Fact]
    public async Task Vote_OutsideTheGeofence_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-far@example.com", PresenceProof.Geofence);

        using HttpClient a = owner;

        // Act — about 1.3 km north of the venue.
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a,
            eventId,
            suggestionId,
            new { coordinates = new { latitude = 48.8686, longitude = 2.3522 } });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>A coordinate inside the radius is admitted.</summary>
    [Fact]
    public async Task Vote_InsideTheGeofence_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-near@example.com", PresenceProof.Geofence);

        using HttpClient a = owner;

        // Act — about 20 m from the venue.
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a,
            eventId,
            suggestionId,
            new { coordinates = new { latitude = 48.85678, longitude = 2.3522 } });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Outside the window nothing admits anybody, however good the coordinate — server time decides.
    /// </summary>
    [Fact]
    public async Task Vote_OutsideTheWindow_Returns422Async()
    {
        // Arrange — a window that closed an hour ago.
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-closed@example.com",
            PresenceProof.Geofence,
            startsAtUtc: DateTime.UtcNow.AddHours(-3),
            endsAtUtc: DateTime.UtcNow.AddHours(-1));

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a,
            eventId,
            suggestionId,
            new { coordinates = new { latitude = 48.85678, longitude = 2.3522 } });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>A valid code admits a voter.</summary>
    [Fact]
    public async Task Vote_WithAValidEventCode_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-code@example.com", PresenceProof.EventCode);

        using HttpClient a = owner;
        string code = await EventQueueRequests.IssueCodeAsync(a, eventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a, eventId, suggestionId, new { eventCode = code });

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>A code nobody issued is refused, with the same answer every other failure gets.</summary>
    [Fact]
    public async Task Vote_WithAnUnknownCode_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-unknown-code@example.com", PresenceProof.EventCode);

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a, eventId, suggestionId, new { eventCode = "ZZZZZZZZ" });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>An expired code is refused: the TTL means something.</summary>
    [Fact]
    public async Task Vote_WithAnExpiredCode_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-expired-code@example.com", PresenceProof.EventCode);

        using HttpClient a = owner;
        string code = await EventQueueRequests.IssueCodeAsync(a, eventId, ttlSeconds: 60);

        // Expire it in the database rather than waiting a minute: what is under test is that the server refuses
        // a code whose moment has passed, not the clock itself.
        await ExpireCodesAsync(eventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a, eventId, suggestionId, new { eventCode = code });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>One account may spend one code once.</summary>
    [Fact]
    public async Task Vote_ReusingACodeFromTheSameAccount_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid first) = await VenueEventAsync(
            "presence-reuse@example.com", PresenceProof.EventCode);

        using HttpClient a = owner;
        Guid second = await EventQueueRequests.SuggestAsync(a, eventId, "bbbbbbbbbbb");
        string code = await EventQueueRequests.IssueCodeAsync(a, eventId);

        using HttpResponseMessage spent = await EventQueueRequests.VoteAsync(
            a, eventId, first, new { eventCode = code });
        Assert.Equal(HttpStatusCode.Created, spent.StatusCode);

        // Act — the same code, the same account, a different suggestion.
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a, eventId, second, new { eventCode = code });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// But another account may spend the same code: one code is read out to a whole room — ADR-0014.
    /// </summary>
    [Fact]
    public async Task Vote_WithTheSameCodeFromAnotherAccount_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-shared-code@example.com", PresenceProof.EventCode);
        (HttpClient guest, _) = await TestBearer.SignInAsync(_factory, _mail, "presence-roommate@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        string code = await EventQueueRequests.IssueCodeAsync(a, eventId);

        using HttpResponseMessage mine = await EventQueueRequests.VoteAsync(
            a, eventId, suggestionId, new { eventCode = code });
        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);

        // Act
        using HttpResponseMessage theirs = await EventQueueRequests.VoteAsync(
            b, eventId, suggestionId, new { eventCode = code });

        // Assert
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
    }

    /// <summary>`Both` means both: a code alone does not admit a voter who is elsewhere.</summary>
    [Fact]
    public async Task Vote_WithBothRequired_AndOnlyACode_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, Guid suggestionId) = await VenueEventAsync(
            "presence-both@example.com", PresenceProof.Both);

        using HttpClient a = owner;
        string code = await EventQueueRequests.IssueCodeAsync(a, eventId);

        // Act
        using HttpResponseMessage response = await EventQueueRequests.VoteAsync(
            a, eventId, suggestionId, new { eventCode = code });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Codes are the owner's to issue.</summary>
    [Fact]
    public async Task IssueEventCode_AsANonOwner_Returns404Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, _) = await VenueEventAsync(
            "presence-code-owner@example.com", PresenceProof.EventCode);
        (HttpClient guest, _) = await TestBearer.SignInAsync(_factory, _mail, "presence-code-guest@example.com");

        using HttpClient a = owner;
        using HttpClient b = guest;

        // Act
        using HttpResponseMessage response = await EventQueueRequests.PostCodeAsync(b, eventId, 900);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A TTL outside the bounds is refused — ADR-0024's expiry discipline.</summary>
    [Fact]
    public async Task IssueEventCode_WithATooLongTtl_Returns422Async()
    {
        // Arrange
        (HttpClient owner, Guid eventId, _) = await VenueEventAsync(
            "presence-long-ttl@example.com", PresenceProof.EventCode);

        using HttpClient a = owner;

        // Act — a day, which is a password rather than evidence of being there now.
        using HttpResponseMessage response = await EventQueueRequests.PostCodeAsync(a, eventId, 86_400);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>A restricted event at the venue, open now, with one suggestion in it.</summary>
    private async Task<(HttpClient Owner, Guid EventId, Guid SuggestionId)> VenueEventAsync(
        string email,
        PresenceProof proof,
        DateTime? startsAtUtc = null,
        DateTime? endsAtUtc = null)
    {
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            EventRequests.EventsPath,
            new
            {
                name = "At the venue",
                startsAtUtc = startsAtUtc ?? DateTime.UtcNow.AddHours(-1),
                endsAtUtc = endsAtUtc ?? DateTime.UtcNow.AddHours(1),
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
                presenceProof = proof,
                geofence = proof == PresenceProof.EventCode
                    ? null
                    : new
                    {
                        latitude = VenueLatitude,
                        longitude = VenueLongitude,
                        radiusMetres = VenueRadiusMetres,
                    },
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        EventRequests.CreatedEvent? body = await created.Content.ReadFromJsonAsync<EventRequests.CreatedEvent>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        Guid suggestionId = await EventQueueRequests.SuggestAsync(owner, body.EventId);

        return (owner, body.EventId, suggestionId);
    }

    /// <summary>Pushes every code of one event into the past.</summary>
    private async Task ExpireCodesAsync(Guid eventId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        await context.EventCodes
            .Where(code => code.EventId == eventId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(code => code.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)),
                TestContext.Current.CancellationToken);
    }
}
