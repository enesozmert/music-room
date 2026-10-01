namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// An event's suggestions, its votes, and the queue they rank — <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <remarks>
/// There is no vote counter. The queue counts the vote rows in SQL on every read, because a stored count is a
/// second copy of a fact two writers can disagree about — SPEC-018 decision 2, SDD §12.2.
/// </remarks>
public interface IEventQueueRepository
{
    /// <summary>Adds a suggestion.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="youtubeVideoId">The video, as the client sent it.</param>
    /// <param name="suggestedByUserId">Who suggested it.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, and the new suggestion's id on success.</returns>
    Task<SuggestionWriteResult> TrySuggestAsync(
        Guid eventId,
        string youtubeVideoId,
        Guid suggestedByUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Removes a suggestion, if the caller is its suggester or the event's owner.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed. Its votes go with it.</returns>
    Task<bool> TryRemoveSuggestionAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Records one account's vote for one suggestion.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <param name="userId">The voter, from the validated token.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// <see cref="VoteWriteOutcome.Recorded"/> the first time and <see cref="VoteWriteOutcome.Unchanged"/>
    /// when the vote was already there — including when a concurrent request wrote it, which the unique index
    /// detects. Both are success: a vote is an idempotent fact.
    /// </returns>
    Task<VoteWriteOutcome> TryCastVoteAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Removes one account's own vote.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <param name="userId">The voter.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> TryWithdrawVoteAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>The queue: suggestions ordered by vote count, then by when they were suggested.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller, so each row can say whether they voted.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the endpoint.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page, counted from the vote rows.</returns>
    /// <remarks>
    /// Ties break on the suggestion's creation time, so the order is total and stable: two tracks on the same
    /// count must not swap places between two reads.
    /// </remarks>
    Task<IReadOnlyList<QueueEntry>> ListQueueAsync(
        Guid eventId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Whether a suggestion belongs to this event.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the event holds that suggestion.</returns>
    Task<bool> SuggestionExistsAsync(
        Guid eventId,
        Guid suggestionId,
        CancellationToken cancellationToken);
}

/// <summary>What a suggestion write did, and the row it created.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="SuggestionId">The new suggestion, when one was added.</param>
public sealed record SuggestionWriteResult(SuggestionWriteOutcome Outcome, Guid? SuggestionId)
{
    /// <summary>The event already holds this video.</summary>
    public static SuggestionWriteResult Duplicate { get; } = new(SuggestionWriteOutcome.Duplicate, null);

    /// <summary>
    /// No such event, or one whose licence does not admit this caller. One value for both, because a distinct
    /// answer would confirm that an event the caller may not touch exists.
    /// </summary>
    public static SuggestionWriteResult NotPermitted { get; } =
        new(SuggestionWriteOutcome.NotPermitted, null);
}

/// <summary>The three ways a suggestion write ends.</summary>
public enum SuggestionWriteOutcome
{
    /// <summary>No such event, or one this caller may not write to — the endpoint answers `404`.</summary>
    NotPermitted = 0,

    /// <summary>Added.</summary>
    Added = 1,

    /// <summary>The event already holds this video — the endpoint answers `409`.</summary>
    Duplicate = 2,
}

/// <summary>What a vote write did.</summary>
public enum VoteWriteOutcome
{
    /// <summary>No such suggestion in this event — the endpoint answers `404`.</summary>
    NotFound = 0,

    /// <summary>The vote was written — the endpoint answers `201`.</summary>
    Recorded = 1,

    /// <summary>The vote was already there — the endpoint answers `200`, because a vote is idempotent.</summary>
    Unchanged = 2,

    /// <summary>
    /// The licence requires presence and the caller did not prove it — the endpoint answers `422`. Never
    /// written by the repository: it is the verifier's answer, carried on the same enum so the endpoint has one
    /// thing to map (ADR-0014, SPEC-018 decision 6).
    /// </summary>
    PresenceNotProved = 3,
}

/// <summary>One row of the vote-ordered queue.</summary>
/// <param name="SuggestionId">The suggestion.</param>
/// <param name="YoutubeVideoId">The video.</param>
/// <param name="SuggestedByUserId">Who suggested it.</param>
/// <param name="Votes">How many accounts have voted for it, counted from the rows.</param>
/// <param name="CallerVoted">Whether the caller is one of them, so a client can render a toggle.</param>
public sealed record QueueEntry(
    Guid SuggestionId,
    string YoutubeVideoId,
    Guid SuggestedByUserId,
    int Votes,
    bool CallerVoted);
