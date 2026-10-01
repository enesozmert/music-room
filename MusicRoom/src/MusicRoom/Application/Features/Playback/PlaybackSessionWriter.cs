using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playback.Commands.StartSession;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Playback;

/// <summary>
/// The playback writes, with their gates and their choice of what plays next — ADR-0017.
/// </summary>
/// <remarks>
/// One collaborator rather than four handlers each wiring the gate, the queue reader and the store: the order of
/// the checks **is** the design, and it should exist once. The handlers are left with their own job — the audit
/// outcome and the broadcast.
/// <para>
/// Every move is a guarded `UPDATE` keyed on the `xmin` the caller read, which is the arbiter of ADR-0017's
/// fourth concurrency hotspot: two clients advancing at once resolve to one move and one `412`, never a double
/// skip.
/// </para>
/// </remarks>
public sealed class PlaybackSessionWriter
{
    private readonly PlaybackControlGate _gate;
    private readonly IPlaybackQueueReader _queue;
    private readonly IPlaybackSessionRepository _sessions;
    private readonly IDeviceRepository _devices;

    /// <summary>Takes the gate, the queue reader, the session store and the device read path.</summary>
    /// <param name="gate">Who may command, and who may see the source.</param>
    /// <param name="queue">What plays next — one abstraction over two orderings.</param>
    /// <param name="sessions">Where sessions live.</param>
    /// <param name="devices">Where the owner of a device is read.</param>
    public PlaybackSessionWriter(
        PlaybackControlGate gate,
        IPlaybackQueueReader queue,
        IPlaybackSessionRepository sessions,
        IDeviceRepository devices)
    {
        _gate = gate;
        _queue = queue;
        _sessions = sessions;
        _devices = devices;
    }

    /// <summary>Opens a session on a device the caller owns, playing a source they may see.</summary>
    /// <param name="source">The device, and what it is to play.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, the session, and what it started on.</returns>
    public async Task<SessionStart> StartAsync(
        PlaybackSource source,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        DeviceControl? device = await _devices
            .FindControlAsync(source.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        // The owner opens a session, not the delegate: starting playback on somebody's speaker is a different
        // act from steering what it plays, and the contract gives this row to `owner of the device`.
        if (device?.OwnerId != userId)
        {
            return new SessionStart(SessionStartOutcome.NotPermitted, null, null);
        }

        if (!await _gate
                .MaySeeSourceAsync(source.SourceType, source.SourceId, userId, cancellationToken)
                .ConfigureAwait(false))
        {
            // The same `404` a stranger gets: a session must not be a way to learn that a private source exists.
            return new SessionStart(SessionStartOutcome.NotPermitted, null, null);
        }

        // The server picks what plays first, from the source's own order — never the client.
        Guid? first = await _queue
            .FindNextAsync(source.SourceType, source.SourceId, afterItemId: null, cancellationToken)
            .ConfigureAwait(false);

        DateTime nowUtc = DateTime.UtcNow;

        PlaybackSession session = new()
        {
            Id = Guid.NewGuid(),
            DeviceId = source.DeviceId,
            SourceType = source.SourceType,
            SourceId = source.SourceId,

            // An empty source still opens a session, with nothing playing. A room that starts the music before
            // anybody has suggested a track is a room waiting for the first suggestion, not an error.
            NowPlayingId = first,
            StartedAtUtc = nowUtc,
        };

        bool started = await _sessions.TryStartAsync(session, cancellationToken).ConfigureAwait(false);

        return started
            ? new SessionStart(SessionStartOutcome.Started, session.Id, first)
            : new SessionStart(SessionStartOutcome.AlreadyPlaying, null, null);
    }

    /// <summary>Moves a session to the next item the source offers.</summary>
    /// <param name="move">Which session, what the caller believes is playing, and the version they hold.</param>
    /// <param name="userId">The caller, who must hold control of the device.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>What happened, and where the session now is.</returns>
    /// <remarks>
    /// Two kinds of staleness, two answers — SPEC-021 decision 4. A stale `expectedNowPlayingId` means this is a
    /// retry of a move that already happened, so nothing moves and the current state is returned; a stale `xmin`
    /// means somebody else moved first, which is a `412`.
    /// </remarks>
    public async Task<PlaybackWriteResult> AdvanceAsync(
        PlaybackMove move,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);

        PlaybackSessionDetail? session = await CommandableAsync(move.SessionId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return PlaybackWriteResult.NotFound;
        }

        // The retry guard. A duplicate advance from a flaky network finds its expectation already stale and is
        // told where the session is, rather than skipping a second track.
        if (session.NowPlayingId != move.ExpectedNowPlayingId)
        {
            return new PlaybackWriteResult(
                PlaybackWriteOutcome.AlreadyMoved,
                session.NowPlayingId,
                session.SessionId);
        }

        Guid? next = await _queue
            .FindNextAsync(session.SourceType, session.SourceId, session.NowPlayingId, cancellationToken)
            .ConfigureAwait(false);

        if (next is null)
        {
            // Nothing left. The session ends — SDD §11.2's `Ended` — and the endpoint answers `409`.
            bool ended = await _sessions
                .TryEndAsync(session.SessionId, move.ExpectedVersion, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            return ended
                ? new PlaybackWriteResult(PlaybackWriteOutcome.QueueExhausted, null, session.SessionId)
                : PlaybackWriteResult.Stale;
        }

        return await WriteAsync(session.SessionId, next.Value, move.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Jumps a session to a named item of its own source.</summary>
    /// <param name="move">Which session, which item, and the version the caller holds.</param>
    /// <param name="userId">The caller, who must hold control of the device.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>What happened, and where the session now is.</returns>
    public async Task<PlaybackWriteResult> JumpAsync(
        PlaybackJump move,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);

        PlaybackSessionDetail? session = await CommandableAsync(move.SessionId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return PlaybackWriteResult.NotFound;
        }

        // Only inside the source. A jump to somebody else's track would make the session a pointer at anything.
        if (!await _queue
                .ContainsAsync(session.SourceType, session.SourceId, move.NowPlayingId, cancellationToken)
                .ConfigureAwait(false))
        {
            return PlaybackWriteResult.NotFound;
        }

        return await WriteAsync(session.SessionId, move.NowPlayingId, move.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Stops the live session of a device the caller owns.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="userId">The caller, who must own the device.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The session that was stopped and its source, or null when there was nothing to stop.</returns>
    public async Task<PlaybackSessionDetail?> StopAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await _sessions
            .FindAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || session.EndedAtUtc is not null)
        {
            return null;
        }

        DeviceControl? device = await _devices
            .FindControlAsync(session.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        // Stopping is the owner's: a delegate steers what plays, and does not get to switch the speaker off.
        if (device?.OwnerId != userId)
        {
            return null;
        }

        bool ended = await _sessions
            .TryEndByDeviceAsync(session.DeviceId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return ended ? session : null;
    }

    /// <summary>The session, when it is live and this caller may command it.</summary>
    private async Task<PlaybackSessionDetail?> CommandableAsync(
        Guid sessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await _sessions
            .FindAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || session.EndedAtUtc is not null)
        {
            return null;
        }

        return await _gate.MayCommandAsync(session.DeviceId, userId, cancellationToken).ConfigureAwait(false)
            ? session
            : null;
    }

    private async Task<PlaybackWriteResult> WriteAsync(
        Guid sessionId,
        Guid nowPlayingId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        bool moved = await _sessions
            .TrySetNowPlayingAsync(sessionId, nowPlayingId, expectedVersion, cancellationToken)
            .ConfigureAwait(false);

        return moved
            ? new PlaybackWriteResult(PlaybackWriteOutcome.Advanced, nowPlayingId, sessionId)
            : PlaybackWriteResult.Stale;
    }
}

/// <summary>An advance: which session, what the caller believes is playing, and the version they hold.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="ExpectedNowPlayingId">
/// What the client believes is playing. A mismatch means this is a retry of a move that already happened, which
/// is success and not a failure — ADR-0017.
/// </param>
/// <param name="ExpectedVersion">The `xmin` the caller last read, from `If-Match`.</param>
public sealed record PlaybackMove(Guid SessionId, Guid? ExpectedNowPlayingId, uint ExpectedVersion);

/// <summary>An explicit jump: which session, which item, and the version the caller holds.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="NowPlayingId">The item to play, which must belong to the session's source.</param>
/// <param name="ExpectedVersion">The `xmin` the caller last read, from `If-Match`.</param>
public sealed record PlaybackJump(Guid SessionId, Guid NowPlayingId, uint ExpectedVersion);
