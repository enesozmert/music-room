using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for playback sessions.
/// </summary>
/// <remarks>
/// Every move is a conditional `UPDATE` carrying the `xmin` the caller read, and the live-session invariant is a
/// filtered unique index rather than a check — so both of ADR-0017's correctness claims are the database's to
/// keep.
/// </remarks>
internal sealed class PlaybackSessionRepository : IPlaybackSessionRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public PlaybackSessionRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<bool> TryStartAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        _context.PlaybackSessions.Add(session);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException exception) when (IsAlreadyPlaying(exception))
        {
            _context.Entry(session).State = EntityState.Detached;

            // The device already has a live session. The filtered index decided it, which is what makes the
            // `409` a fact rather than a read-then-write guess.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PlaybackSessionDetail?> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await Detail(_context.PlaybackSessions.Where(session => session.Id == sessionId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PlaybackSessionDetail?> FindByDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken) =>
        await Detail(_context.PlaybackSessions
                .Where(session => session.DeviceId == deviceId && session.EndedAtUtc == null))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PlaybackSessionDetail?> FindLiveByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        // The newest live one — SPEC-058 decision 5. At most one live session exists per **device**, not
        // per event, so two devices may legitimately play one event; a guest arriving now should follow
        // the one that started last.
        await Detail(_context.PlaybackSessions
                .Where(session => session.SourceType == PlaybackSourceType.Event
                    && session.SourceId == eventId
                    && session.EndedAtUtc == null)
                .OrderByDescending(session => session.StartedAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TrySetNowPlayingAsync(
        Guid sessionId,
        Guid nowPlayingId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // The token and "still live" are both in the predicate, so a second advance arriving at the same moment
        // matches nothing and is told `412` — the loser of ADR-0017's fourth hotspot never skips a track.
        int moved = await _context.PlaybackSessions
            .Where(session => session.Id == sessionId
                && session.EndedAtUtc == null
                && session.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.NowPlayingId, nowPlayingId)

                    // In the same statement as the id it describes — SPEC-058 decision 1. Two statements
                    // could disagree, and the disagreement would be a track that started at a time it did not.
                    .SetProperty(session => session.NowPlayingStartedAtUtc, (DateTime?)nowUtc)

                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context would
                    // have stamped is stamped here — SDD §8.3.
                    .SetProperty(session => session.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return moved > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryEndAsync(
        Guid sessionId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int ended = await _context.PlaybackSessions
            .Where(session => session.Id == sessionId
                && session.EndedAtUtc == null
                && session.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.NowPlayingId, (Guid?)null)

                    // Nothing is playing, so nothing started — SPEC-058 decision 2.
                    .SetProperty(session => session.NowPlayingStartedAtUtc, (DateTime?)null)
                    .SetProperty(session => session.EndedAtUtc, nowUtc)
                    .SetProperty(session => session.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return ended > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryEndByDeviceAsync(
        Guid deviceId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int ended = await _context.PlaybackSessions
            .Where(session => session.DeviceId == deviceId && session.EndedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.NowPlayingId, (Guid?)null)

                    // Nothing is playing, so nothing started — SPEC-058 decision 2.
                    .SetProperty(session => session.NowPlayingStartedAtUtc, (DateTime?)null)
                    .SetProperty(session => session.EndedAtUtc, nowUtc)
                    .SetProperty(session => session.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return ended > 0;
    }

    /// <summary>
    /// The projection every read shares, with the server's own clock on it — SPEC-058 decision 4.
    /// </summary>
    /// <remarks>
    /// `nowUtc` is captured before the query rather than written as `DateTime.UtcNow` inside the
    /// projection: a value captured here travels as a parameter, and one written inline would be
    /// translated into whatever the provider makes of it. What the client needs is the instant this
    /// response was composed, which is this one.
    /// </remarks>
    private static IQueryable<PlaybackSessionDetail> Detail(IQueryable<PlaybackSession> sessions)
    {
        DateTime nowUtc = DateTime.UtcNow;

        return sessions
            .AsNoTracking()
            .Select(session => new PlaybackSessionDetail(
                session.Id,
                session.DeviceId,
                session.SourceType,
                session.SourceId,
                session.NowPlayingId,
                session.StartedAtUtc,
                session.EndedAtUtc,
                session.Version,
                session.NowPlayingStartedAtUtc,
                nowUtc));
    }

    private static bool IsAlreadyPlaying(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgresException.ConstraintName,
            "ux_playback_sessions_device_id",
            StringComparison.Ordinal);
}
