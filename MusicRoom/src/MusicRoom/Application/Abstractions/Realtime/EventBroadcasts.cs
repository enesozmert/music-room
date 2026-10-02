namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// One fact about one event's queue — SDD §9.2, ADR-0036.
/// </summary>
/// <param name="EventId">The event the listeners are grouped by.</param>
/// <param name="OccurredAtUtc">
/// Server time when the change committed. An ordering hint, not a version: it tells a client which of two
/// messages is newer and never pretends to let it detect a gap.
/// </param>
/// <remarks>
/// **There is no version here, deliberately** — ADR-0036. An event's `xmin` is already the `If-Match` of its
/// metadata `PATCH`, so touching the event row on every vote would stop its owner renaming it during the
/// evening it runs; and a `queue_version` column would reintroduce the shared mutable count SPEC-018 removed.
/// <para>
/// Instead each payload carries the **authoritative value of what changed**, which for a count is stronger
/// than a version: a client that missed three messages and receives `votes: 7` is correct, where a client
/// that applied `+1` three times out of order is not. Clients re-read the queue on connect and reconnect,
/// which is the reconcile point a version would otherwise provide.
/// </para>
/// </remarks>
public abstract record EventBroadcast(Guid EventId, DateTime OccurredAtUtc) : IRealtimeBroadcast
{
    /// <inheritdoc />
    public Guid AggregateId => EventId;
}

/// <summary>A track was suggested — `TrackSuggested`.</summary>
/// <param name="EventId">The event.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="SuggestionId">The new suggestion.</param>
/// <param name="YoutubeVideoId">The video, so a client can render without a second call.</param>
/// <remarks>
/// Idempotent by id: applying it twice is applying it once, which is why no version is needed to make it
/// safe to replay.
/// </remarks>
public sealed record TrackSuggested(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid SuggestionId,
    string YoutubeVideoId) : EventBroadcast(EventId, OccurredAtUtc);

/// <summary>A vote was cast or withdrawn — `VoteChanged`.</summary>
/// <param name="EventId">The event.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="SuggestionId">The suggestion whose count moved.</param>
/// <param name="Votes">
/// The suggestion's **new count**, counted from the vote rows inside the transaction that changed them.
/// State, not a delta — ADR-0036.
/// </param>
/// <remarks>
/// Who voted is not in the payload. A listener needs the count to rank the queue and has no business
/// learning which of the people in the room voted for what; a client that wants to know whether **it** voted
/// reads that from the queue, about itself.
/// </remarks>
public sealed record VoteChanged(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid SuggestionId,
    int Votes) : EventBroadcast(EventId, OccurredAtUtc);

/// <summary>A suggestion left the queue — `QueueReordered`.</summary>
/// <param name="EventId">The event.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="RemovedSuggestionId">The suggestion that is gone, with its votes.</param>
/// <remarks>
/// Sent on a removal and not on a vote: a vote already sends <see cref="VoteChanged"/> with the new count,
/// and the order follows from counts the client already holds. Sending both would send one fact twice
/// (SPEC-019 decision 4).
/// </remarks>
public sealed record QueueReordered(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid RemovedSuggestionId) : EventBroadcast(EventId, OccurredAtUtc);
