using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Commands.StopSession;

/// <summary>
/// Ends the session, if the caller owns its device.
/// </summary>
internal sealed class StopSessionCommandHandler : IRequestHandler<StopSessionCommand, bool>
{
    private readonly PlaybackSessionWriter _writer;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the writer, the caller, the audit context and the broadcast queue.</summary>
    /// <param name="writer">The gate and the end.</param>
    /// <param name="currentUser">Who is stopping it, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public StopSessionCommandHandler(
        PlaybackSessionWriter writer,
        ICurrentUserAccessor currentUser,
        IAuditContext audit,
        IBroadcastQueue broadcasts)
    {
        _writer = writer;
        _currentUser = currentUser;
        _audit = audit;
        _broadcasts = broadcasts;
    }

    /// <summary>Stops it, or reports that there was nothing this caller could stop.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a session was ended.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(StopSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "StopSessionCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        _audit.SetTargetId(request.SessionId);

        PlaybackSessionDetail? stopped = await _writer
            .StopAsync(request.SessionId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (stopped is null)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return false;
        }

        // A null now-playing is the honest end: listeners stop rather than waiting for a track that is not
        // coming.
        _broadcasts.Enqueue(new NowPlayingChanged(
            stopped.SourceType,
            stopped.SourceId,
            stopped.SessionId,
            NowPlayingId: null,
            DateTime.UtcNow));

        return true;
    }
}
