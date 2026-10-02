using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Features.Events.Queries.AuthoriseJoin;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// The live vote-ordered queue — SDD §9.2, ENDPOINTS §9, ADR-0004.
/// </summary>
/// <remarks>
/// Transport only, exactly as <see cref="PlaylistHub"/> is: the two methods validate an argument, dispatch one
/// CQRS message and return. Every mutation — suggesting, voting, withdrawing — goes through the REST routes and
/// their one authorisation and validation pipeline (ENDPOINTS §10).
/// <para>
/// The `member` gate decides a join: who may hear about an event is who may see it. Voting is a stricter gate
/// and belongs to the write, which is the only place that can rule on presence (ADR-0014).
/// </para>
/// <para>
/// Its payloads carry no row version — ADR-0036. An event's `xmin` is already the `If-Match` of its metadata
/// `PATCH`, so each fact carries the authoritative value of what changed instead, and clients re-read the queue
/// on connect.
/// </para>
/// </remarks>
[Authorize]
public sealed class EventHub : Hub
{
    /// <summary>
    /// Where the hub is mapped. Under <see cref="Middleware.ClientContextMiddleware.HubPaths"/>, which is
    /// what makes the handshake carry platform, device and appVersion or be refused.
    /// </summary>
    public const string Path = "/hubs/events";

    /// <summary>The `TrackSuggested` event name.</summary>
    public const string TrackSuggestedEvent = "TrackSuggested";

    /// <summary>The `VoteChanged` event name.</summary>
    public const string VoteChangedEvent = "VoteChanged";

    /// <summary>The `QueueReordered` event name.</summary>
    public const string QueueReorderedEvent = "QueueReordered";

    /// <summary>The wire name of the join method — ENDPOINTS §9.</summary>
    public const string JoinMethod = "JoinEvent";

    /// <summary>The wire name of the leave method — ENDPOINTS §9.</summary>
    public const string LeaveMethod = "LeaveEvent";

    private readonly ISender _sender;

    /// <summary>Takes the dispatcher the two methods send through.</summary>
    /// <param name="sender">MediatR, so the hub holds no data access of its own.</param>
    public EventHub(ISender sender) => _sender = sender;

    /// <summary>The group one event's listeners share.</summary>
    /// <param name="eventId">The event.</param>
    /// <returns>The group name, <c>event:{eventId}</c> — SDD §9.2.</returns>
    public static string Group(Guid eventId) =>
        string.Create(CultureInfo.InvariantCulture, $"event:{eventId}");

    /// <summary>Captures the client context, or refuses the connection — ADR-0019.</summary>
    /// <returns>A task that completes when the connection is accepted.</returns>
    /// <exception cref="HubException">
    /// The handshake carried no platform, device and app version. The middleware has already answered `400`
    /// for that, so reaching here without them means the pipeline was bypassed — refuse rather than write
    /// audit rows nobody can attribute.
    /// </exception>
    public override async Task OnConnectedAsync()
    {
        HubClientContext context = HubClientContext.FromHandshake(Context.GetHttpContext())
            ?? throw new HubException(
                "A hub handshake must carry platform, device and appVersion — ENDPOINTS §9.");

        Context.Items[HubClientContext.ItemKey] = context;

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>Joins the group for one event, if the caller may see it.</summary>
    /// <param name="eventId">The event.</param>
    /// <returns>A task that completes when the connection is in the group.</returns>
    /// <exception cref="HubException">
    /// The caller may not see this event, or there is no such event. One message for both, as the REST `404` is
    /// one answer for both: a distinct refusal would confirm the event exists.
    /// </exception>
    [HubMethodName(JoinMethod)]
    public async Task JoinEventAsync(Guid eventId)
    {
        bool mayJoin = await _sender
            .Send(new AuthoriseEventJoinQuery(eventId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (!mayJoin)
        {
            throw new HubException("No such event.");
        }

        await Groups
            .AddToGroupAsync(Context.ConnectionId, Group(eventId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>Leaves the group for one event.</summary>
    /// <param name="eventId">The event.</param>
    /// <returns>A task that completes when the connection has left.</returns>
    /// <remarks>
    /// Unauthorised on purpose: leaving a group tells nobody anything, and refusing to let a client stop
    /// listening would be a worse answer than letting it.
    /// </remarks>
    [HubMethodName(LeaveMethod)]
    public Task LeaveEventAsync(Guid eventId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(eventId), Context.ConnectionAborted);
}
