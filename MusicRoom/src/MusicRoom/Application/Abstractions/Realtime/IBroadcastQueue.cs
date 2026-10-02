namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// What this request wants broadcast once its transaction has committed — ADR-0035.
/// </summary>
/// <remarks>
/// One queue for every hub. A handler states what happened and knows nothing about transports or timing;
/// <c>BroadcastBehaviour</c> decides when, and <see cref="IRealtimeBroadcaster"/> decides where.
/// </remarks>
/// <remarks>
/// A handler states what happened; it does not decide when anybody hears about it. The handler runs inside
/// the transaction <c>AuditBehaviour</c> opened, so sending from there would announce state that a rollback
/// can still take away. <c>BroadcastBehaviour</c> wraps that transaction and drains this queue after it
/// commits.
/// </remarks>
public interface IBroadcastQueue
{
    /// <summary>Records a fact to send after the commit.</summary>
    /// <param name="broadcast">What happened.</param>
    void Enqueue(IRealtimeBroadcast broadcast);

    /// <summary>Takes everything queued, leaving the queue empty.</summary>
    /// <returns>The facts, in the order they were enqueued.</returns>
    /// <remarks>
    /// Draining empties the queue, so a nested pipeline cannot send the same fact twice and a request that
    /// threw leaves nothing behind for the next one — the scope ends with the request either way.
    /// </remarks>
    IReadOnlyList<IRealtimeBroadcast> Drain();
}

/// <summary>
/// The request-scoped queue. A plain list: one request, one thread of execution, no I/O.
/// </summary>
public sealed class BroadcastQueue : IBroadcastQueue
{
    private readonly List<IRealtimeBroadcast> _pending = [];

    /// <inheritdoc />
    public void Enqueue(IRealtimeBroadcast broadcast)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        _pending.Add(broadcast);
    }

    /// <inheritdoc />
    public IReadOnlyList<IRealtimeBroadcast> Drain()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        IRealtimeBroadcast[] drained = [.. _pending];
        _pending.Clear();

        return drained;
    }
}
