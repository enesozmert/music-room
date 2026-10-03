using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Domain.Common;
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
    /// <summary>
    /// The `NowPlayingChanged` event name, which both hubs publish — SDD §9.2.
    /// </summary>
    /// <remarks>
    /// Declared here rather than on either hub, because it is the one event that belongs to both: a session plays
    /// from an event or from a playlist, and its listeners are grouped by whichever it is.
    /// </remarks>
    internal const string NowPlayingChangedEvent = "NowPlayingChanged";

    private readonly IHubContext<PlaylistHub> _playlists;
    private readonly IHubContext<EventHub> _events;
    private readonly IHubContext<ControlHub> _control;
    private readonly ILogger<RealtimeBroadcaster> _logger;

    /// <summary>Takes both hub contexts and the log.</summary>
    /// <param name="playlists">Where playlist groups live.</param>
    /// <param name="events">Where event groups live.</param>
    /// <param name="control">Where the per-account control groups live.</param>
    /// <param name="logger">Where an undelivered notification is reported.</param>
    public RealtimeBroadcaster(
        IHubContext<PlaylistHub> playlists,
        IHubContext<EventHub> events,
        IHubContext<ControlHub> control,
        ILogger<RealtimeBroadcaster> logger)
    {
        _playlists = playlists;
        _events = events;
        _control = control;
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

    /// <summary>The group and the event name one payload goes to — SDD §9.2 names all eight.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A payload type with no route. It is a programming error, not a delivery failure, so it is not swallowed
    /// with the two exceptions above.
    /// </exception>
    /// <remarks>
    /// One table per hub, because one table for all of them is a switch the complexity gate refuses — and because
    /// a reader looking for "what does PlaylistHub publish" should find one list, not a filter.
    /// </remarks>
    private (IClientProxy Group, string EventName) Route(IRealtimeBroadcast broadcast) => broadcast switch
    {
        PlaylistBroadcast => (PlaylistGroup(broadcast), PlaylistEventName(broadcast)),
        EventBroadcast => (EventGroup(broadcast), EventEventName(broadcast)),

        // The group key here is a **person**, not a shared aggregate: `AggregateId` carries the recipient, which
        // is what `user:{userId}` means — SPEC-022 decision 1.
        ControlBroadcast => (ControlGroup(broadcast), ControlEventName(broadcast)),

        // The one payload that belongs to both: a session plays from an event or from a playlist, and its
        // listeners are grouped by whichever it is — SDD §9.2.
        NowPlayingChanged playback => (
            playback.SourceType == PlaybackSourceType.Event
                ? EventGroup(broadcast)
                : PlaylistGroup(broadcast),
            NowPlayingChangedEvent),

        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No hub route is declared for this payload — SDD §9.2."),
    };

    private static string PlaylistEventName(IRealtimeBroadcast broadcast) => broadcast switch
    {
        TrackAdded => PlaylistHub.TrackAddedEvent,
        TrackMoved => PlaylistHub.TrackMovedEvent,
        TrackRemoved => PlaylistHub.TrackRemovedEvent,
        CollaboratorChanged => PlaylistHub.CollaboratorChangedEvent,
        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No PlaylistHub event name is declared for this payload — SDD §9.2."),
    };

    private static string EventEventName(IRealtimeBroadcast broadcast) => broadcast switch
    {
        TrackSuggested => EventHub.TrackSuggestedEvent,
        VoteChanged => EventHub.VoteChangedEvent,
        QueueReordered => EventHub.QueueReorderedEvent,
        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No EventHub event name is declared for this payload — SDD §9.2."),
    };

    private static string ControlEventName(IRealtimeBroadcast broadcast) => broadcast switch
    {
        ControlGranted => ControlHub.ControlGrantedEvent,
        ControlRevoked => ControlHub.ControlRevokedEvent,
        ControlTakenOver => ControlHub.ControlTakenOverEvent,
        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No ControlHub event name is declared for this payload — SDD §9.2."),
    };

    private IClientProxy ControlGroup(IRealtimeBroadcast broadcast) =>
        _control.Clients.Group(ControlHub.Group(broadcast.AggregateId));

    private IClientProxy PlaylistGroup(IRealtimeBroadcast broadcast) =>
        _playlists.Clients.Group(PlaylistHub.Group(broadcast.AggregateId));

    private IClientProxy EventGroup(IRealtimeBroadcast broadcast) =>
        _events.Clients.Group(EventHub.Group(broadcast.AggregateId));
}
