using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Queries.ListSessions;

/// <summary>
/// Reads the caller's live sessions — SDD §13.2 gate 1, the <c>self</c> tier.
/// </summary>
internal sealed class ListSessionsQueryHandler
    : IRequestHandler<ListSessionsQuery, IReadOnlyList<SessionSummary>>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the refresh-token read path and the caller.</summary>
    /// <param name="refreshTokens">Where sessions live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListSessionsQueryHandler(
        IRefreshTokenRepository refreshTokens,
        ICurrentUserAccessor currentUser)
    {
        _refreshTokens = refreshTokens;
        _currentUser = currentUser;
    }

    /// <summary>Lists the caller's live sessions, newest first.</summary>
    /// <param name="request">The query, which carries nothing.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per live family of the calling account.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller. The route requires
    /// authorisation, so reaching here without one means the pipeline was bypassed, and returning an empty
    /// list would hide that.</exception>
    public Task<IReadOnlyList<SessionSummary>> Handle(
        ListSessionsQuery request,
        CancellationToken cancellationToken)
    {
        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListSessionsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-010.");

        return _refreshTokens.ListSessionsAsync(userId, DateTime.UtcNow, cancellationToken);
    }
}
