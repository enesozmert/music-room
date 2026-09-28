using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Commands.CancelFriendRequest;

/// <summary>
/// Deletes a pending request the caller sent — only the sender may withdraw.
/// </summary>
internal sealed class CancelFriendRequestCommandHandler : IRequestHandler<CancelFriendRequestCommand, bool>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the graph, the caller and the audit context.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is withdrawing, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public CancelFriendRequestCommandHandler(
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _friendships = friendships;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Withdraws it, if the caller sent it and it is still pending.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a pending request of the caller's was deleted.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(CancelFriendRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid senderId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "CancelFriendRequestCommand reached its handler with no authenticated caller. The route "
                + "requires authorisation — SPEC-012.");

        _audit.SetTargetId(request.RequestId);

        bool cancelled = await _friendships
            .TryCancelRequestAsync(senderId, request.RequestId, cancellationToken)
            .ConfigureAwait(false);

        if (!cancelled)
        {
            // Includes a recipient trying to cancel: they may decline, which is a different row and a
            // different verb.
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return cancelled;
    }
}
