using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Commands.ReportPosition;

/// <summary>
/// Records the playing device's position and tells the room — SPEC-059.
/// </summary>
/// <remarks>
/// The authorisation is the jump route's: the device's owner, or whoever holds its control grant.
/// Reporting where the music is, is something only the thing playing it can honestly do, and a guest who
/// could write it could make every other guest seek to a lie.
/// </remarks>
internal sealed class ReportPositionCommandHandler : IRequestHandler<ReportPositionCommand, bool>
{
    private readonly IPlaybackSessionRepository _sessions;
    private readonly PlaybackControlGate _gate;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the write path, the gate, the caller and the broadcast queue.</summary>
    /// <param name="sessions">Where sessions and positions live.</param>
    /// <param name="gate">Whether the caller may command the session's device.</param>
    /// <param name="currentUser">Who is reporting, from the validated token.</param>
    /// <param name="broadcasts">Where the event goes once the row is written.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ReportPositionCommandHandler(
        IPlaybackSessionRepository sessions,
        PlaybackControlGate gate,
        ICurrentUserAccessor currentUser,
        IBroadcastQueue broadcasts)
    {
        _sessions = sessions;
        _gate = gate;
        _currentUser = currentUser;
        _broadcasts = broadcasts;
    }

    /// <summary>Writes the report, or reports that there was nothing to write it against.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was written; false for every refusal, which the endpoint answers `404`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(ReportPositionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ReportPositionCommand reached its handler with no authenticated caller. The route "
                + "requires authorisation — SPEC-059.");

        PlaybackSessionDetail? session = await _sessions
            .FindAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || session.EndedAtUtc is not null)
        {
            return false;
        }

        bool mayCommand = await _gate
            .MayCommandAsync(session.DeviceId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!mayCommand)
        {
            // The same answer an unknown session gets. A caller learns nothing about sessions they do
            // not control, which is the rule the jump route already follows.
            return false;
        }

        return await WriteAsync(request, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the report and tells the room, or reports that the session moved on.</summary>
    /// <remarks>
    /// One instant for the row and for the message, so a listener and a later reader of the session agree
    /// about when the device said what it said.
    /// </remarks>
    private async Task<bool> WriteAsync(
        ReportPositionCommand request,
        PlaybackSessionDetail session,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        bool written = await _sessions
            .TryReportPositionAsync(
                new PlaybackPositionReport(
                    request.SessionId,
                    request.NowPlayingId,
                    request.PositionMs,
                    request.Paused),
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (!written)
        {
            // The session moved on while this report was in flight. Nothing is written and nothing is
            // broadcast: a listener acting on it would seek backwards into the previous track.
            return false;
        }

        _broadcasts.Enqueue(new PlaybackPositionChanged(
            session.SourceType,
            session.SourceId,
            request.SessionId,
            request.NowPlayingId,
            request.PositionMs,
            nowUtc,
            request.Paused));

        return true;
    }
}
