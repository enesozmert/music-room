using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Commands.AdvanceSession;

/// <summary>
/// Moves the session on, if the caller holds control of its device.
/// </summary>
internal sealed class AdvanceSessionCommandHandler
    : IRequestHandler<AdvanceSessionCommand, PlaybackWriteResult>
{
    private readonly PlaybackSessionWriter _writer;
    private readonly IPlaybackSessionRepository _sessions;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the writer, the session read path, the caller, the audit context and the queue.</summary>
    /// <param name="writer">The gates, the next item and the guarded write.</param>
    /// <param name="sessions">Read again for the source the broadcast is grouped by.</param>
    /// <param name="currentUser">Who is advancing, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AdvanceSessionCommandHandler(
        PlaybackSessionWriter writer,
        IPlaybackSessionRepository sessions,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _writer = writer;
        _sessions = sessions;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Advances it, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>What happened, and where the session now is.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaybackWriteResult> Handle(
        AdvanceSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "AdvanceSessionCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        _audit.SetTargetId(request.SessionId);

        PlaybackWriteResult result = await _writer
            .AdvanceAsync(
                new PlaybackMove(request.SessionId, request.ExpectedNowPlayingId, request.ExpectedVersion),
                userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome is PlaybackWriteOutcome.NotFound or PlaybackWriteOutcome.VersionMismatch)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return result;
        }

        // A retry that moved nothing tells nobody anything: every listener already holds this now-playing.
        if (result.Outcome == PlaybackWriteOutcome.AlreadyMoved)
        {
            return result;
        }

        await EnqueueAsync(request.SessionId, result.NowPlayingId, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Queues `NowPlayingChanged` for the source the session plays from.
    /// </summary>
    /// <remarks>
    /// The session is read again rather than carried through the writer: the broadcast is grouped by the
    /// **source**, and a handler that guessed the group from the request would be a handler that broadcasts a
    /// playlist's change to an event's listeners.
    /// </remarks>
    private async Task EnqueueAsync(
        Guid sessionId,
        Guid? nowPlayingId,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await _sessions
            .FindAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return;
        }

        _broadcasts.Enqueue(new NowPlayingChanged(
            session.SourceType,
            session.SourceId,
            sessionId,
            nowPlayingId,
            DateTime.UtcNow));
    }
}
