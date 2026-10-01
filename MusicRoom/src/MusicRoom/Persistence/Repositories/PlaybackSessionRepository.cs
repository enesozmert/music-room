using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
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
    public async Task<bool> TrySetNowPlayingAsync(
        Guid sessionId,
        Guid nowPlayingId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        // The token and "still live" are both in the predicate, so a second advance arriving at the same moment
        // matches nothing and is told `412` — the loser of ADR-0017's fourth hotspot never skips a track.
        int moved = await _context.PlaybackSessions
            .Where(session => session.Id == sessionId
                && session.EndedAtUtc == null
                && session.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.NowPlayingId, nowPlayingId)
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
                    .SetProperty(session => session.EndedAtUtc, nowUtc)
                    .SetProperty(session => session.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return ended > 0;
    }

    private static IQueryable<PlaybackSessionDetail> Detail(IQueryable<PlaybackSession> sessions) =>
        sessions
            .AsNoTracking()
            .Select(session => new PlaybackSessionDetail(
                session.Id,
                session.DeviceId,
                session.SourceType,
                session.SourceId,
                session.NowPlayingId,
                session.StartedAtUtc,
                session.EndedAtUtc,
                session.Version));

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
