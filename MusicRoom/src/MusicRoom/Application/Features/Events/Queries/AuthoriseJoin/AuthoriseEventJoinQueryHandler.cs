using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Queries.AuthoriseJoin;

/// <summary>
/// Answers the `member` gate for a hub join — the same gate the REST read uses.
/// </summary>
internal sealed class AuthoriseEventJoinQueryHandler : IRequestHandler<AuthoriseEventJoinQuery, bool>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the gate and the caller.</summary>
    /// <param name="events">Where the `member` gate is decided.</param>
    /// <param name="currentUser">Who is asking — on this transport, from the hub's own principal.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AuthoriseEventJoinQueryHandler(IEventRepository events, ICurrentUserAccessor currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    /// <summary>True when the caller may see the event, and so may hear about it.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The connection's cancellation token.</param>
    /// <returns>Whether the join is allowed.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(AuthoriseEventJoinQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "AuthoriseEventJoinQuery reached its handler with no authenticated caller. The hub requires "
                + "authorisation — SPEC-019.");

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // Seeing it is enough to hear about it. Voting is a stricter gate, enforced by the write that needs
        // it — the hub carries no mutations.
        return access.MaySee;
    }
}
