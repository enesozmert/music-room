using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Playback.Commands.StartSession;

/// <summary>
/// Opens the session, if the caller owns the device and may see the source.
/// </summary>
internal sealed class StartSessionCommandHandler : IRequestHandler<StartSessionCommand, SessionStart>
{
    private readonly PlaybackSessionWriter _writer;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IBroadcastQueue _broadcasts;

    /// <summary>Takes the writer, the caller, the audit context and the broadcast queue.</summary>
    /// <param name="writer">The gates, the queue reader and the session store, as one collaborator.</param>
    /// <param name="currentUser">Who is starting playback, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <param name="broadcasts">What the listeners are told, once this has committed — ADR-0035.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public StartSessionCommandHandler(
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

    /// <summary>Starts it, or reports why not.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome, the session and what it started on.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<SessionStart> Handle(
        StartSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "StartSessionCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        SessionStart start = await _writer
            .StartAsync(request.Source, userId, cancellationToken)
            .ConfigureAwait(false);

        if (start.Outcome != SessionStartOutcome.Started)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return start;
        }

        _audit.SetTargetId(start.SessionId!.Value);

        _broadcasts.Enqueue(new NowPlayingChanged(
            request.Source.SourceType,
            request.Source.SourceId,
            start.SessionId.Value,
            start.NowPlayingId,
            DateTime.UtcNow));

        return start;
    }
}
