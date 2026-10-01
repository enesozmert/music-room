using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusicRoom.Domain.Common;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The event requests the three event suites share — SPEC-017.
/// </summary>
/// <remarks>
/// Its own file: inlining them would put each suite past the 300-line limit the root .editorconfig enforces.
/// </remarks>
internal static class EventRequests
{
    /// <summary>Where the event routes live.</summary>
    internal const string EventsPath = "/api/v1/events";

    /// <summary>When the sample event begins.</summary>
    internal static readonly DateTime Starts = new(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>When the sample event ends.</summary>
    internal static readonly DateTime Ends = new(2026, 11, 1, 23, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates an event and asserts the `201`.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="visibility">Who may find it.</param>
    /// <param name="licence">Who may vote in it.</param>
    /// <returns>The new event's id.</returns>
    internal static async Task<Guid> CreateAsync(
        HttpClient client,
        string name,
        Visibility visibility = Visibility.Private,
        LicenceMode licence = LicenceMode.InvitedOnly)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            EventsPath,
            new
            {
                name,
                startsAtUtc = Starts,
                endsAtUtc = Ends,
                visibility,
                licence,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedEvent? created = await response.Content.ReadFromJsonAsync<CreatedEvent>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        return created.EventId;
    }

    /// <summary>Posts an arbitrary creation payload, for the cases that must be refused.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="payload">The body.</param>
    /// <returns>The response, for the caller to assert on.</returns>
    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, object payload)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(EventsPath, payload, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads one event.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <returns>The response, for the caller to assert on.</returns>
    internal static async Task<HttpResponseMessage> GetAsync(HttpClient client, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.GetAsync($"{EventsPath}/{eventId}", TestContext.Current.CancellationToken);
    }

    /// <summary>The `ETag` of one event.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <returns>The token the next write must send back.</returns>
    internal static async Task<string> ETagAsync(HttpClient client, Guid eventId)
    {
        using HttpResponseMessage response = await GetAsync(client, eventId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        return response.Headers.ETag.Tag;
    }

    /// <summary>One guarded `PATCH` of an event's metadata.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="name">The new name.</param>
    /// <param name="etag">The token the caller holds.</param>
    /// <returns>The response, for the caller to assert on.</returns>
    internal static async Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        Guid eventId,
        string name,
        string etag)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpRequestMessage request = new(HttpMethod.Patch, $"{EventsPath}/{eventId}")
        {
            Content = JsonContent.Create(new { name, startsAtUtc = Starts, endsAtUtc = Ends }),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Invites somebody.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">Who to invite.</param>
    /// <returns>The response, for the caller to assert on.</returns>
    internal static async Task<HttpResponseMessage> InviteAsync(
        HttpClient client,
        Guid eventId,
        Guid userId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(
            $"{EventsPath}/{eventId}/invitations",
            new { userId },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Joins an event's roster.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <returns>The response, for the caller to assert on.</returns>
    internal static async Task<HttpResponseMessage> AttendAsync(HttpClient client, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(
            $"{EventsPath}/{eventId}/attendances",
            new { },
            TestContext.Current.CancellationToken);
    }

    /// <summary>What `POST /events` answers with.</summary>
    /// <param name="EventId">The new event.</param>
    internal sealed record CreatedEvent(Guid EventId);

    /// <summary>One event, as a client reads it.</summary>
    /// <param name="EventId">The event.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Licence">Who may vote in it.</param>
    /// <param name="Geofence">The venue, when the licence has one.</param>
    internal sealed record EventBody(
        Guid EventId,
        string Name,
        LicenceMode Licence,
        GeofenceBody? Geofence);

    /// <summary>A venue, as a client reads it.</summary>
    /// <param name="Latitude">The latitude.</param>
    /// <param name="Longitude">The longitude.</param>
    /// <param name="RadiusMetres">The radius.</param>
    internal sealed record GeofenceBody(decimal Latitude, decimal Longitude, int RadiusMetres);
}
