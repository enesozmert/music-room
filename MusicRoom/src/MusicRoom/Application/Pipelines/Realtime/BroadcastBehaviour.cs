using MediatR;
using MusicRoom.Application.Abstractions.Realtime;

namespace MusicRoom.Application.Pipelines.Realtime;

/// <summary>
/// Sends what the handler queued, once the transaction that caused it has committed — ADR-0035,
/// `signalr-realtime`.
/// </summary>
/// <remarks>
/// Registered **between** <c>ValidationBehaviour</c> and <c>AuditBehaviour</c>, which is the whole
/// mechanism: <c>AuditBehaviour</c> owns the transaction and commits before it returns, so by the time
/// <c>next()</c> completes here the change is durable. Broadcasting from a handler would announce state a
/// rollback can still take away, and there is no message that means "ignore what I just told you".
/// <para>
/// An exception from the inner pipeline propagates with the queue undrained, so a rolled-back action sends
/// nothing. A send that fails is the implementation's problem to log: <see cref="IPlaylistBroadcaster"/>
/// does not throw, because a notification must not turn a committed action into a `500`.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">What the request answers.</typeparam>
public sealed class BroadcastBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPlaylistBroadcastQueue _queue;
    private readonly IPlaylistBroadcaster _broadcaster;

    /// <summary>Takes the request's queue and the transport that sends from it.</summary>
    /// <param name="queue">What the handler asked to be broadcast.</param>
    /// <param name="broadcaster">Where it goes — implemented in <c>WebAPI</c>.</param>
    public BroadcastBehaviour(IPlaylistBroadcastQueue queue, IPlaylistBroadcaster broadcaster)
    {
        _queue = queue;
        _broadcaster = broadcaster;
    }

    /// <summary>Runs the rest of the pipeline, then sends what it queued.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The rest of the pipeline, transaction and all.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Whatever the handler answered, unchanged.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        TResponse response = await next(cancellationToken).ConfigureAwait(false);

        foreach (PlaylistBroadcast broadcast in _queue.Drain())
        {
            await _broadcaster.SendAsync(broadcast, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
