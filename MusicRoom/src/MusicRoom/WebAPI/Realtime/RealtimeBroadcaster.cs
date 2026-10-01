using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Realtime;

/// <summary>
/// Routes a fact to the hub and group that carry it — ADR-0035, SDD §9.2.
/// </summary>
/// <remarks>
/// One sender for both hubs, chosen by the payload's own type. The <c>Application</c> layer hands over a fact
/// and an aggregate id; which hub, which group name and which event name are decided here, where SignalR is.
/// <para>
/// It does not throw. By the time it runs the action is durable; a notification that could not be delivered
/// must not turn a completed action into a `500`. A client that misses a message converges on its next read —
/// a playlist fact carries the version that makes the gap visible, and an event fact carries the value itself
/// (ADR-0036).
/// </para>
/// </remarks>
internal sealed class RealtimeBroadcaster : IRealtimeBroadcaster
{
    private readonly IHubContext<PlaylistHub> _playlists;
    private readonly IHubContext<EventHub> _events;
    private readonly ILogger<RealtimeBroadcaster> _logger;

    /// <summary>Takes both hub contexts and the log.</summary>
    /// <param name="playlists">Where playlist groups live.</param>
    /// <param name="events">Where event groups live.</param>
    /// <param name="logger">Where an undelivered notification is reported.</param>
    public RealtimeBroadcaster(
        IHubContext<PlaylistHub> playlists,
        IHubContext<EventHub> events,
        ILogger<RealtimeBroadcaster> logger)
    {
        _playlists = playlists;
        _events = events;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(IRealtimeBroadcast broadcast, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        (IClientProxy group, string eventName) = Route(broadcast);

        try
        {
            await group.SendAsync(eventName, broadcast, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // The caller went away after its write committed. The change stands; nobody is waiting.
            _logger.LogDebug(
                exception,
                "A {Event} broadcast for {AggregateId} was cancelled before it was sent.",
                eventName,
                broadcast.AggregateId);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "A {Event} broadcast for {AggregateId} could not be sent. The change is committed; clients "
                    + "will resynchronise on their next read.",
                eventName,
                broadcast.AggregateId);
        }
    }

    /// <summary>The group and the event name one payload goes to — SDD §9.2 names all seven.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A payload type with no route. It is a programming error, not a delivery failure, so it is not swallowed
    /// with the two exceptions above.
    /// </exception>
    private (IClientProxy Group, string EventName) Route(IRealtimeBroadcast broadcast) => broadcast switch
    {
        TrackAdded => (PlaylistGroup(broadcast), PlaylistHub.TrackAddedEvent),
        TrackMoved => (PlaylistGroup(broadcast), PlaylistHub.TrackMovedEvent),
        TrackRemoved => (PlaylistGroup(broadcast), PlaylistHub.TrackRemovedEvent),
        CollaboratorChanged => (PlaylistGroup(broadcast), PlaylistHub.CollaboratorChangedEvent),

        TrackSuggested => (EventGroup(broadcast), EventHub.TrackSuggestedEvent),
        VoteChanged => (EventGroup(broadcast), EventHub.VoteChangedEvent),
        QueueReordered => (EventGroup(broadcast), EventHub.QueueReorderedEvent),

        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No hub route is declared for this payload — SDD §9.2."),
    };

    private IClientProxy PlaylistGroup(IRealtimeBroadcast broadcast) =>
        _playlists.Clients.Group(PlaylistHub.Group(broadcast.AggregateId));

    private IClientProxy EventGroup(IRealtimeBroadcast broadcast) =>
        _events.Clients.Group(EventHub.Group(broadcast.AggregateId));
}
