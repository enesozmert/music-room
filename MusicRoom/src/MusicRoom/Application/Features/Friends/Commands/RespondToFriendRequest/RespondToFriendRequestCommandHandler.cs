using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Friends.Commands.RespondToFriendRequest;

/// <summary>
/// Answers a pending friend request, creating the friendship on acceptance.
/// </summary>
internal sealed class RespondToFriendRequestCommandHandler
    : IRequestHandler<RespondToFriendRequestCommand, bool>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the graph, the caller and the audit context.</summary>
    /// <param name="friendships">The friends graph.</param>
    /// <param name="currentUser">Who is answering, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public RespondToFriendRequestCommandHandler(
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _friendships = friendships;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Accepts or declines, if the request is the caller's to answer.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a pending request of the caller's was answered.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<bool> Handle(
        RespondToFriendRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid recipientId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "RespondToFriendRequestCommand reached its handler with no authenticated caller. The route "
                + "requires authorisation — SPEC-012.");

        _audit.SetTargetId(request.RequestId);

        bool answered = await _friendships
            .TryRespondToRequestAsync(
                recipientId,
                request.RequestId,
                request.Accept,
                DateTime.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (!answered)
        {
            // Unknown, already answered, or somebody else's — one outcome, so neither the row nor the response
            // says which.
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return answered;
    }
}
