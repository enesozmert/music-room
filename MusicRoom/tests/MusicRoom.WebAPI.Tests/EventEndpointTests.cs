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
/// The event aggregate: creation, the window, the licence policy and the `ETag` — SPEC-017, subject V.2.1.
/// </summary>
/// <remarks>
/// The tests that matter most are the refusals around `LocationAndTimeRestricted`: a licence that claims to
/// restrict by place and time with nothing configured to check would read as a feature and behave as an open
/// door (ADR-0014, SPEC-017 decision 3).
/// </remarks>
public sealed class EventEndpointTests : IAsyncLifetime
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

    /// <summary>The aggregate exists, and its owner can read it back with an `ETag`.</summary>
    [Fact]
    public async Task Create_ThenRead_ReturnsTheEventToItsOwnerAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-owner@example.com");

        using HttpClient a = owner;

        // Act
        Guid eventId = await EventRequests.CreateAsync(a, "Launch party");

        using HttpResponseMessage read = await EventRequests.GetAsync(a, eventId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);

        EventRequests.EventBody? body = await read.Content.ReadFromJsonAsync<EventRequests.EventBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Launch party", body.Name);
        Assert.Null(body.Geofence);
    }

    /// <summary>An event that ends before it starts is refused — SPEC-017 decision 4.</summary>
    [Fact]
    public async Task Create_WithAnEndBeforeTheStart_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-backwards@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventRequests.PostAsync(
            a,
            new
            {
                name = "Backwards",
                startsAtUtc = EventRequests.Ends,
                endsAtUtc = EventRequests.Starts,
            });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["endsAtUtc"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>
    /// The restricted licence with no presence policy is refused — decision 3, the rule this spec exists for.
    /// </summary>
    [Fact]
    public async Task Create_WithTheRestrictedLicenceAndNoPolicy_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-unchecked@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventRequests.PostAsync(
            a,
            new
            {
                name = "Venue only",
                startsAtUtc = EventRequests.Starts,
                endsAtUtc = EventRequests.Ends,
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
            });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["presenceProof"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>A geofence proof with no centre is refused for the same reason, one level down.</summary>
    [Fact]
    public async Task Create_WithAGeofenceProofAndNoCentre_Returns422Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-no-centre@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage response = await EventRequests.PostAsync(
            a,
            new
            {
                name = "No centre",
                startsAtUtc = EventRequests.Starts,
                endsAtUtc = EventRequests.Ends,
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
                presenceProof = PresenceProof.Geofence,
            });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["geofence"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>An update that ends before it starts is `422` keyed on `endsAtUtc` — SPEC-036.</summary>
    /// <remarks>
    /// The create route proves the same rule above. This proves the **update** route is wired to it, which is what
    /// sharing `EventLicenceRules` between the two is supposed to guarantee.
    /// </remarks>
    [Fact]
    public async Task Patch_WithAnEndBeforeTheStart_Returns422KeyedOnEndsAtUtcAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-patch-backwards@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Forwards");
        string etag = await EventRequests.ETagAsync(a, eventId);

        using HttpRequestMessage request = new(HttpMethod.Patch, $"{EventRequests.EventsPath}/{eventId}")
        {
            Content = JsonContent.Create(
                new { name = "Backwards", startsAtUtc = EventRequests.Ends, endsAtUtc = EventRequests.Starts }),
        };

        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag));

        // Act
        using HttpResponseMessage response = await a.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["endsAtUtc"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>
    /// Setting the restricted licence with no presence policy is `422` keyed on `presenceProof` — SPEC-036.
    /// </summary>
    /// <remarks>
    /// `PUT /events/{eventId}/licence` had **no `422` test anywhere** before this one, while publishing an
    /// `errors` body from the same shared rules the create route uses.
    /// </remarks>
    [Fact]
    public async Task PutLicence_WithTheRestrictedLicenceAndNoPolicy_Returns422KeyedOnPresenceProofAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-licence-nopolicy@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Venue");

        // Act
        using HttpResponseMessage response = await a.PutAsJsonAsync(
            $"{EventRequests.EventsPath}/{eventId}/licence",
            new { visibility = Visibility.Public, licence = LicenceMode.LocationAndTimeRestricted },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["presenceProof"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>A full geofence policy is stored and read back, not silently dropped.</summary>
    [Fact]
    public async Task Create_WithAGeofencePolicy_StoresTheCentreAndRadiusAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-geofence@example.com");

        using HttpClient a = owner;

        // Act
        using HttpResponseMessage created = await EventRequests.PostAsync(
            a,
            new
            {
                name = "At the venue",
                startsAtUtc = EventRequests.Starts,
                endsAtUtc = EventRequests.Ends,
                visibility = Visibility.Public,
                licence = LicenceMode.LocationAndTimeRestricted,
                presenceProof = PresenceProof.Geofence,
                geofence = new { latitude = 48.8566m, longitude = 2.3522m, radiusMetres = 250 },
            });

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        EventRequests.CreatedEvent? body =
            await created.Content.ReadFromJsonAsync<EventRequests.CreatedEvent>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        using HttpResponseMessage read = await EventRequests.GetAsync(a, body.EventId);
        EventRequests.EventBody? stored = await read.Content.ReadFromJsonAsync<EventRequests.EventBody>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.NotNull(stored.Geofence);
        Assert.Equal(48.8566m, stored.Geofence.Latitude);
        Assert.Equal(250, stored.Geofence.RadiusMetres);
    }

    /// <summary>A fresh token succeeds and the token changes, so the next stale write fails.</summary>
    [Fact]
    public async Task Patch_WithAFreshIfMatch_Succeeds_AndTheETagChangesAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-fresh-etag@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Before");
        string etag = await EventRequests.ETagAsync(a, eventId);

        // Act
        using HttpResponseMessage renamed = await EventRequests.PatchAsync(a, eventId, "After", etag);

        // Assert
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.NotEqual(etag, await EventRequests.ETagAsync(a, eventId));
    }

    /// <summary>A stale token changes nothing and answers `412`.</summary>
    [Fact]
    public async Task Patch_WithAStaleIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-stale-etag@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Original");
        string stale = await EventRequests.ETagAsync(a, eventId);

        using HttpResponseMessage first = await EventRequests.PatchAsync(a, eventId, "First writer", stale);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using HttpResponseMessage second = await EventRequests.PatchAsync(a, eventId, "Second writer", stale);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
    }

    /// <summary>No `If-Match` is treated as a mismatch, as on a playlist.</summary>
    [Fact]
    public async Task Patch_WithNoIfMatch_Returns412Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-no-etag@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Unguarded");

        // Act
        using HttpResponseMessage response = await a.PatchAsJsonAsync(
            $"{EventRequests.EventsPath}/{eventId}",
            new { name = "Sneaky", startsAtUtc = EventRequests.Starts, endsAtUtc = EventRequests.Ends },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    /// <summary>A deleted event disappears from every read.</summary>
    [Fact]
    public async Task Delete_AsTheOwner_HidesItFromEveryReadAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-deleted@example.com");

        using HttpClient a = owner;
        Guid eventId = await EventRequests.CreateAsync(a, "Doomed");

        // Act
        using HttpResponseMessage deleted = await a.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using HttpResponseMessage read = await EventRequests.GetAsync(a, eventId);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    /// <summary>Creating an event is audited — ADR-0033 on a new surface.</summary>
    [Fact]
    public async Task CreateEvent_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "event-audited@example.com");

        using HttpClient a = owner;

        // Act
        Guid eventId = await EventRequests.CreateAsync(a, "Audited");

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "event.create",
                TestContext.Current.CancellationToken);

        Assert.NotNull(record.UserId);
        Assert.Equal(eventId, record.TargetId);
        Assert.Equal("event", record.TargetType);
    }
}
