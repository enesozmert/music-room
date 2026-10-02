using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Playback.Queries.GetSession;

/// <summary>
/// Reads a session for a caller who may see its source.
/// </summary>
internal sealed class GetSessionQueryHandler : IRequestHandler<GetSessionQuery, PlaybackSessionDetail?>
{
    private readonly IPlaybackSessionRepository _sessions;
    private readonly PlaybackControlGate _gate;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path, the gate and the caller.</summary>
    /// <param name="sessions">Where sessions live.</param>
    /// <param name="gate">Whether the caller may see the source.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetSessionQueryHandler(
        IPlaybackSessionRepository sessions,
        PlaybackControlGate gate,
        ICurrentUserAccessor currentUser)
    {
        _sessions = sessions;
        _gate = gate;
        _currentUser = currentUser;
    }

    /// <summary>Reads it, or answers null.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The session, or null when the caller may not see its source.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaybackSessionDetail?> Handle(
        GetSessionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetSessionQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-021.");

        PlaybackSessionDetail? session = await _sessions
            .FindAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        return await _gate
            .MaySeeSourceAsync(session.SourceType, session.SourceId, userId, cancellationToken)
            .ConfigureAwait(false)
                ? session
                : null;
    }
}
