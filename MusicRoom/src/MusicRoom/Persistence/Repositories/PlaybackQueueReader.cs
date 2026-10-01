using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// What plays next, in each kind of source — ADR-0017, subject V.2.1.
/// </summary>
/// <remarks>
/// Two orderings behind one question. An **event** plays the highest vote count, ties broken by the earliest
/// suggestion — the same order the queue read publishes, which is what makes "a highly-voted track plays earlier"
/// true rather than claimed. A **playlist** plays the next position.
/// <para>
/// An event advances by **rank**, not by a cursor: the next item is the highest-voted suggestion that is not the
/// one playing and has not already been overtaken, which for a live vote means the queue can reorder under the
/// session and the next track is still the one the room wants most. A playlist advances by position, because its
/// order is the thing a human arranged.
/// </para>
/// </remarks>
internal sealed class PlaybackQueueReader : IPlaybackQueueReader
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public PlaybackQueueReader(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Guid?> FindNextAsync(
        PlaybackSourceType sourceType,
        Guid sourceId,
        Guid? afterItemId,
        CancellationToken cancellationToken) =>
        sourceType == PlaybackSourceType.Event
            ? NextSuggestionAsync(sourceId, afterItemId, cancellationToken)
            : NextTrackAsync(sourceId, afterItemId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ContainsAsync(
        PlaybackSourceType sourceType,
        Guid sourceId,
        Guid itemId,
        CancellationToken cancellationToken) =>
        sourceType == PlaybackSourceType.Event
            ? await _context.EventSuggestions
                .AsNoTracking()
                .AnyAsync(
                    suggestion => suggestion.Id == itemId && suggestion.EventId == sourceId,
                    cancellationToken)
                .ConfigureAwait(false)
            : await _context.PlaylistTracks
                .AsNoTracking()
                .AnyAsync(track => track.Id == itemId && track.PlaylistId == sourceId, cancellationToken)
                .ConfigureAwait(false);

    /// <summary>The event's most-voted suggestion other than the one playing.</summary>
    private async Task<Guid?> NextSuggestionAsync(
        Guid eventId,
        Guid? afterItemId,
        CancellationToken cancellationToken) =>
        await _context.EventSuggestions
            .AsNoTracking()
            .Where(suggestion => suggestion.EventId == eventId && suggestion.Id != afterItemId)
            .OrderByDescending(suggestion =>
                _context.EventVotes.Count(vote => vote.SuggestionId == suggestion.Id))
            .ThenBy(suggestion => suggestion.CreatedAtUtc)
            .Select(suggestion => (Guid?)suggestion.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>The playlist's next track by position, or its first when nothing is playing.</summary>
    private async Task<Guid?> NextTrackAsync(
        Guid playlistId,
        Guid? afterItemId,
        CancellationToken cancellationToken)
    {
        long? currentPosition = afterItemId is null
            ? null
            : await _context.PlaylistTracks
                .AsNoTracking()
                .Where(track => track.Id == afterItemId && track.PlaylistId == playlistId)
                .Select(track => (long?)track.Position)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        return await _context.PlaylistTracks
            .AsNoTracking()
            .Where(track => track.PlaylistId == playlistId
                // A track that is no longer in the playlist leaves `currentPosition` null, so the session
                // resumes at the top rather than stalling on a row that was removed under it.
                && (currentPosition == null || track.Position > currentPosition))
            .OrderBy(track => track.Position)
            .ThenBy(track => track.CreatedAtUtc)
            .Select(track => (Guid?)track.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
