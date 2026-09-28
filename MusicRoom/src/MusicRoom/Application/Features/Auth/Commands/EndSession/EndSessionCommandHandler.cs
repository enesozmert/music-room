using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.EndSession;

/// <summary>
/// Revokes one refresh-token family of the calling account — SDD §13.1.
/// </summary>
/// <remarks>
/// The access token already issued for that session stays valid until it expires, at most 15 minutes and 30
/// seconds after it was signed. That is the accepted window in T-28 and it applies to logout for the same
/// reason: a stateless token cannot be recalled without state on every request.
/// </remarks>
internal sealed class EndSessionCommandHandler : IRequestHandler<EndSessionCommand, EndSessionOutcome>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the refresh-token write path, the caller and the audit context.</summary>
    /// <param name="refreshTokens">Where sessions live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public EndSessionCommandHandler(
        IRefreshTokenRepository refreshTokens,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _refreshTokens = refreshTokens;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Ends the named session, or the caller's current one.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Ended, or not found — which also covers somebody else's session.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller, or a request for "current" from a
    /// token with no <c>sid</c>. Both mean the pipeline was bypassed, and acting anyway would revoke a
    /// session on a guess.</exception>
    public async Task<EndSessionOutcome> Handle(
        EndSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "EndSessionCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-010.");

        Guid sessionId = request.SessionId
            ?? _currentUser.SessionId
            ?? throw new InvalidOperationException(
                "EndSessionCommand asked for the current session, and the validated token carries no sid "
                + "claim — SPEC-010 escalation 1.");

        _audit.SetTargetId(sessionId);

        bool ended = await _refreshTokens
            .TryRevokeFamilyAsync(userId, sessionId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!ended)
        {
            // Unknown, already ended, or another account's — one outcome, so the row and the response both
            // stay silent about which.
            _audit.SetOutcome(AuditOutcome.Refused);

            return EndSessionOutcome.NotFound;
        }

        return EndSessionOutcome.Ended;
    }
}
