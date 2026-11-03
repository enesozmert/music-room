using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Playback.Queries.GetEventSession;

/// <summary>
/// Reads the event's live session for a caller who may see the event — SPEC-058.
/// </summary>
/// <remarks>
/// The visibility check comes **first**, and that ordering is the whole privacy argument: an event a
/// caller may not see answers null whether or not anything is playing, so the route cannot be used to
/// learn that a private event exists or that it has music on.
/// </remarks>
internal sealed class GetEventSessionQueryHandler : IRequestHandler<GetEventSessionQuery, PlaybackSessionDetail?>
{
    private readonly IPlaybackSessionRepository _sessions;
    private readonly PlaybackControlGate _gate;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path, the gate and the caller.</summary>
    /// <param name="sessions">Where sessions live.</param>
    /// <param name="gate">Whether the caller may see the event.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetEventSessionQueryHandler(
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
    /// <returns>The newest live session, or null when there is none or the caller may not see the event.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<PlaybackSessionDetail?> Handle(
        GetEventSessionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetEventSessionQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-058.");

        bool maySee = await _gate
            .MaySeeSourceAsync(PlaybackSourceType.Event, request.EventId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!maySee)
        {
            return null;
        }

        return await _sessions
            .FindLiveByEventAsync(request.EventId, cancellationToken)
            .ConfigureAwait(false);
    }
}
