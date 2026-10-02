using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Commands.JumpToItem;

/// <summary>
/// Jumps the session, if the caller holds control and the item belongs to the source.
/// </summary>
internal sealed class JumpToItemCommandHandler : IRequestHandler<JumpToItemCommand, PlaybackWriteResult>
{
    private readonly PlaybackSessionWriter _writer;
    private readonly IPlaybackSessionRepository _sessions;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the writer, the session read path, the caller, the audit context and the queue.</summary>
    /// <param name="writer">The gates and the guarded write.</param>
    /// <param name="sessions">Read again for the source the broadcast is grouped by.</param>
    /// <param name="currentUser">Who is jumping, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public JumpToItemCommandHandler(
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

    /// <summary>Jumps, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>What happened, and where the session now is.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaybackWriteResult> Handle(
        JumpToItemCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "JumpToItemCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        _audit.SetTargetId(request.SessionId);

        PlaybackWriteResult result = await _writer
            .JumpAsync(
                new PlaybackJump(request.SessionId, request.NowPlayingId, request.ExpectedVersion),
                userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome != PlaybackWriteOutcome.Advanced)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return result;
        }

        PlaybackSessionDetail? session = await _sessions
            .FindAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is not null)
        {
            _broadcasts.Enqueue(new NowPlayingChanged(
                session.SourceType,
                session.SourceId,
                request.SessionId,
                request.NowPlayingId,
                DateTime.UtcNow));
        }

        return result;
    }
}
