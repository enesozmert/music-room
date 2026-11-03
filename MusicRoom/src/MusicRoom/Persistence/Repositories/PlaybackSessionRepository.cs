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

    /// <summary>The position rows, which have no property on the context — SPEC-059, S1200.</summary>
    /// <remarks>A property rather than a field, so the naming rule asks for no underscore.</remarks>
    private IQueryable<PlaybackPosition> Positions => _context.Set<PlaybackPosition>().AsNoTracking();

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
    public async Task<bool> TryReportPositionAsync(
        PlaybackPositionReport report,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        // The track is in the predicate, not checked before it — SPEC-059 decision 6. A report that was in
        // flight while the track advanced matches nothing and is discarded, rather than dragging every
        // listener backwards into the previous song.
        bool playing = await _context.PlaybackSessions
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == report.SessionId
                    && session.EndedAtUtc == null
                    && session.NowPlayingId == report.NowPlayingId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!playing)
        {
            return false;
        }

        // An update first, an insert when it matched nothing. The session row is never touched: its `xmin`
        // is the `If-Match` of advance and jump, and a report four times a minute would answer every
        // controller `412` — SPEC-059 decision 1.
        int updated = await _context.Set<PlaybackPosition>()
            .Where(position => position.SessionId == report.SessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(position => position.NowPlayingId, report.NowPlayingId)
                    .SetProperty(position => position.PositionMs, report.PositionMs)
                    .SetProperty(position => position.Paused, report.Paused)
                    .SetProperty(position => position.ReportedAtUtc, nowUtc)
                    .SetProperty(position => position.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return updated > 0 || await InsertPositionAsync(report, nowUtc, cancellationToken).ConfigureAwait(false);
    }

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
    private IQueryable<PlaybackSessionDetail> Detail(IQueryable<PlaybackSession> sessions)
    {
        DateTime nowUtc = DateTime.UtcNow;

        // The position is joined rather than carried on the session row — SPEC-059 decision 1 — and only
        // when it belongs to the track that is playing now. A position for a track that has ended is not
        // stale data to be interpreted by a client; it is nothing, and it reads as null.
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
                nowUtc,
                Positions
                    .Where(position => position.SessionId == session.Id
                        && position.NowPlayingId == session.NowPlayingId)
                    .Select(position => (int?)position.PositionMs)
                    .FirstOrDefault(),
                Positions
                    .Where(position => position.SessionId == session.Id
                        && position.NowPlayingId == session.NowPlayingId)
                    .Select(position => (DateTime?)position.ReportedAtUtc)
                    .FirstOrDefault(),
                Positions
                    .Where(position => position.SessionId == session.Id
                        && position.NowPlayingId == session.NowPlayingId)
                    .Select(position => (bool?)position.Paused)
                    .FirstOrDefault()));
    }

    /// <summary>Writes the first report of a session, losing a race rather than throwing.</summary>
    /// <remarks>
    /// The unique index on `session_id` is what makes one row mean one row. Two first reports arriving
    /// together leave one winner; the loser's value is a quarter of a second old and the next report is a
    /// quarter of a second away, so it is dropped rather than retried.
    /// </remarks>
    private async Task<bool> InsertPositionAsync(
        PlaybackPositionReport report,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        PlaybackPosition position = new()
        {
            Id = Guid.NewGuid(),
            SessionId = report.SessionId,
            NowPlayingId = report.NowPlayingId,
            PositionMs = report.PositionMs,
            Paused = report.Paused,
            ReportedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.Add(position);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException)
        {
            _context.Entry(position).State = EntityState.Detached;

            return false;
        }
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
