using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for an event's suggestions and votes.
/// </summary>
/// <remarks>
/// Nothing here increments anything. A vote is a row; the queue counts the rows. That is what makes the
/// lost-update bug subject V.2.1 grades absent rather than merely unlikely — SPEC-018 decision 2, SDD §12.2.
/// <para>
/// Every duplicate — a second suggestion of one video, a second vote from one account — is decided by a unique
/// index, which is the only arbiter that works under concurrency (`ef-core-persistence`).
/// </para>
/// </remarks>
internal sealed class EventQueueRepository : IEventQueueRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public EventQueueRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<SuggestionWriteResult> TrySuggestAsync(
        Guid eventId,
        string youtubeVideoId,
        Guid suggestedByUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        EventSuggestion suggestion = new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            YoutubeVideoId = youtubeVideoId,
            SuggestedByUserId = suggestedByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.EventSuggestions.Add(suggestion);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new SuggestionWriteResult(SuggestionWriteOutcome.Added, suggestion.Id);
        }
        catch (DbUpdateException exception)
            when (IsDuplicate(exception, "ux_event_suggestions_event_id_youtube_video_id"))
        {
            _context.Entry(suggestion).State = EntityState.Detached;

            return SuggestionWriteResult.Duplicate;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRemoveSuggestionAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Both permitted callers in one predicate: the suggester, or the event's owner through a subquery.
        // The votes go with the row by the cascade on the foreign key — SPEC-018 decision 7.
        int removed = await _context.EventSuggestions
            .Where(suggestion => suggestion.Id == suggestionId
                && suggestion.EventId == eventId
                && (suggestion.SuggestedByUserId == userId
                    || _context.Events.Any(candidate =>
                        candidate.Id == eventId && candidate.OwnerId == userId)))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<VoteWriteResult> TryCastVoteAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!await SuggestionExistsAsync(eventId, suggestionId, cancellationToken).ConfigureAwait(false))
        {
            return VoteWriteResult.NotFound;
        }

        EventVote vote = new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SuggestionId = suggestionId,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.EventVotes.Add(vote);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new VoteWriteResult(
                VoteWriteOutcome.Recorded,
                await CountVotesAsync(suggestionId, cancellationToken).ConfigureAwait(false));
        }
        catch (DbUpdateException exception)
            when (IsDuplicate(exception, "ux_event_votes_event_id_suggestion_id_user_id"))
        {
            _context.Entry(vote).State = EntityState.Detached;

            // Already voted, or a concurrent request got there first. Both are success: a vote is an idempotent
            // fact, and the endpoint answers `200` rather than `409` — SPEC-018 decision 1.
            return new VoteWriteResult(
                VoteWriteOutcome.Unchanged,
                await CountVotesAsync(suggestionId, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc />
    public async Task<VoteWriteResult> TryWithdrawVoteAsync(
        Guid eventId,
        Guid suggestionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        int removed = await _context.EventVotes
            .Where(vote => vote.EventId == eventId
                && vote.SuggestionId == suggestionId
                && vote.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed == 0
            ? VoteWriteResult.NotFound
            : new VoteWriteResult(
                VoteWriteOutcome.Recorded,
                await CountVotesAsync(suggestionId, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueEntry>> ListQueueAsync(
        Guid eventId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await _context.EventSuggestions
            .AsNoTracking()
            .Where(suggestion => suggestion.EventId == eventId)
            .Select(suggestion => new
            {
                suggestion.Id,
                suggestion.YoutubeVideoId,
                suggestion.SuggestedByUserId,
                suggestion.CreatedAtUtc,

                // Counted in SQL from the vote rows themselves. No column holds this, so no two writers can
                // disagree about it — SPEC-018 decision 2.
                Votes = _context.EventVotes.Count(vote => vote.SuggestionId == suggestion.Id),
                CallerVoted = _context.EventVotes.Any(vote =>
                    vote.SuggestionId == suggestion.Id && vote.UserId == userId),
            })

            // Ties break on when the track was suggested, so the order is total and stable: two tracks on the
            // same count must not swap places between two reads.
            .OrderByDescending(row => row.Votes)
            .ThenBy(row => row.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new QueueEntry(
                row.Id,
                row.YoutubeVideoId,
                row.SuggestedByUserId,
                row.Votes,
                row.CallerVoted))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SuggestionExistsAsync(
        Guid eventId,
        Guid suggestionId,
        CancellationToken cancellationToken) =>
        await _context.EventSuggestions
            .AsNoTracking()
            .AnyAsync(
                suggestion => suggestion.Id == suggestionId && suggestion.EventId == eventId,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The suggestion's votes, counted from the rows.
    /// </summary>
    /// <remarks>
    /// Read after the write and inside the same transaction, which is the only moment the number is both
    /// current and the caller's own. It is what the `VoteChanged` broadcast carries — ADR-0036.
    /// </remarks>
    private async Task<int> CountVotesAsync(Guid suggestionId, CancellationToken cancellationToken) =>
        await _context.EventVotes
            .AsNoTracking()
            .CountAsync(vote => vote.SuggestionId == suggestionId, cancellationToken)
            .ConfigureAwait(false);

    private static bool IsDuplicate(DbUpdateException exception, string uniqueIndexName) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(postgresException.ConstraintName, uniqueIndexName, StringComparison.Ordinal);
}
