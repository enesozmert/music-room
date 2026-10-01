namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// Where a live fact goes — SDD §9.2, ADR-0035.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>WebAPI</c>, which is where SignalR lives. This layer knows that
/// something must be told; which hub and which group is the transport's business, decided from the payload's
/// own type.
/// <para>
/// One port for every hub rather than one per hub: the ordering guarantee — send only after the transaction
/// commits — is the valuable part, and it should exist once (SPEC-019 decision 2).
/// </para>
/// <para>
/// An implementation **must not throw.** The database is the arbiter: by the time this is called the action
/// has committed, and a notification that could not be delivered must not turn a completed action into a
/// `500`. An implementation logs its own failure and returns — ADR-0035.
/// </para>
/// </remarks>
public interface IRealtimeBroadcaster
{
    /// <summary>Sends one fact to everybody listening to the aggregate it names.</summary>
    /// <param name="broadcast">What happened.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the send has been attempted.</returns>
    Task SendAsync(IRealtimeBroadcast broadcast, CancellationToken cancellationToken);
}

/// <summary>
/// One fact about one aggregate, ready to be pushed once its transaction has committed.
/// </summary>
/// <remarks>
/// The base carries the **aggregate's id and nothing else**, because that is all the two hubs have in
/// common: a playlist fact carries the row version its `ETag` also carries, and an event fact carries the
/// authoritative value of what changed, because an event's `xmin` is already spoken for by the `If-Match` of
/// its metadata `PATCH` — ADR-0036. A version on the base would force one of the two to lie about what it
/// has.
/// <para>
/// The id is here so the transport can name a group without knowing every payload type, while the group's
/// shape — <c>playlist:{id}</c>, <c>event:{id}</c> — stays in <c>WebAPI</c> where SignalR is.
/// </para>
/// </remarks>
public interface IRealtimeBroadcast
{
    /// <summary>The aggregate the listeners of this fact are grouped by.</summary>
    Guid AggregateId { get; }
}
