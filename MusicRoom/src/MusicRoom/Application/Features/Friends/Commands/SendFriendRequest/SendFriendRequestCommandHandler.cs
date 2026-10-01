using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Commands.SendFriendRequest;

/// <summary>
/// Creates a pending friend request — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
internal sealed class SendFriendRequestCommandHandler
    : IRequestHandler<SendFriendRequestCommand, SendFriendRequestResult>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the graph, the caller and the audit context.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SendFriendRequestCommandHandler(
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _friendships = friendships;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Sends the request, or reports why it could not be sent.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `201`, `409` or `404`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<SendFriendRequestResult> Handle(
        SendFriendRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid senderId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "SendFriendRequestCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-012.");

        _audit.SetTargetId(request.TargetUserId);

        SendFriendRequestResult result = await _friendships
            .TrySendRequestAsync(senderId, request.TargetUserId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status != SendFriendRequestStatus.Created)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return result;
    }
}
