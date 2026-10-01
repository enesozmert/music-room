using Microsoft.AspNetCore.SignalR;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Realtime;

/// <summary>
/// Sends a playlist's facts to its group over SignalR — ADR-0035.
/// </summary>
/// <remarks>
/// The <c>Application</c> port, implemented where the transport is. It is called by
/// <c>BroadcastBehaviour</c>, which runs outside the transaction, so everything sent from here has
/// committed.
/// <para>
/// It does not throw. By the time it runs the action is durable; a notification that could not be delivered
/// must not turn a completed action into a `500`. A client that misses a message sees a version it did not
/// expect on its next read and resynchronises, which is the contract in `signalr-realtime`.
/// </para>
/// </remarks>
internal sealed class PlaylistBroadcaster : IPlaylistBroadcaster
{
    private readonly IHubContext<PlaylistHub> _hub;
    private readonly ILogger<PlaylistBroadcaster> _logger;

    /// <summary>Takes the hub context and the log.</summary>
    /// <param name="hub">Where groups and clients live.</param>
    /// <param name="logger">Where an undelivered notification is reported.</param>
    public PlaylistBroadcaster(IHubContext<PlaylistHub> hub, ILogger<PlaylistBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(PlaylistBroadcast broadcast, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        try
        {
            await _hub.Clients
                .Group(PlaylistHub.Group(broadcast.PlaylistId))
                .SendAsync(EventName(broadcast), broadcast, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // The caller went away after its write committed. The change stands; nobody is waiting.
            _logger.LogDebug(
                exception,
                "A {Event} broadcast for playlist {PlaylistId} was cancelled before it was sent.",
                EventName(broadcast),
                broadcast.PlaylistId);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "A {Event} broadcast for playlist {PlaylistId} could not be sent. The change is committed; "
                    + "clients will resynchronise on their next read.",
                EventName(broadcast),
                broadcast.PlaylistId);
        }
    }

    /// <summary>The event name a payload is sent under — SDD §9.2 names all four.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A payload type with no event name. It is a programming error, not a delivery failure, so it is not
    /// swallowed with the two above.
    /// </exception>
    private static string EventName(PlaylistBroadcast broadcast) => broadcast switch
    {
        TrackAdded => PlaylistHub.TrackAddedEvent,
        TrackMoved => PlaylistHub.TrackMovedEvent,
        TrackRemoved => PlaylistHub.TrackRemovedEvent,
        CollaboratorChanged => PlaylistHub.CollaboratorChangedEvent,
        _ => throw new ArgumentOutOfRangeException(
            nameof(broadcast),
            broadcast.GetType().Name,
            "No hub event name is declared for this payload — SDD §9.2."),
    };
}
