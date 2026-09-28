using MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;
using MusicRoom.Application.Features.Friends.Queries.ListFriends;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The friends graph: requests, friendships, and the reads over both — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <remarks>
/// Every member takes the caller's account id explicitly, and no member accepts a caller id from a request
/// body: the only source of identity is the validated token, so there is no shape of call that reaches another
/// account's graph.
/// </remarks>
public interface IFriendshipRepository
{
    /// <summary>
    /// Creates a pending request, if the pair is allowed one.
    /// </summary>
    /// <param name="senderId">The caller.</param>
    /// <param name="recipientId">The account being asked.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The outcome and, on success, the new request's id. A pending request in **either** direction, an
    /// existing friendship, or a request to oneself is <see cref="SendFriendRequestStatus.Conflict"/>; an
    /// unknown recipient is <see cref="SendFriendRequestStatus.RecipientNotFound"/>.
    /// </returns>
    Task<SendFriendRequestResult> TrySendRequestAsync(
        Guid senderId,
        Guid recipientId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Accepts or declines a pending request addressed to the caller, and on acceptance creates the friendship
    /// in the same transaction.
    /// </summary>
    /// <param name="recipientId">The caller, who must be the recipient.</param>
    /// <param name="requestId">The request to answer.</param>
    /// <param name="accept">True to accept, false to decline.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when a pending request of the caller's was answered. False covers unknown, already answered and
    /// **somebody else's** — one answer, so the endpoint's `404` says nothing about which.
    /// </returns>
    /// <remarks>
    /// Single answer is arbitrated by a conditional <c>UPDATE</c> on <c>status = pending</c>, so twenty parallel
    /// accepts of one request produce exactly one friendship.
    /// </remarks>
    Task<bool> TryRespondToRequestAsync(
        Guid recipientId,
        Guid requestId,
        bool accept,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a pending request the caller sent.</summary>
    /// <param name="senderId">The caller, who must be the sender.</param>
    /// <param name="requestId">The request to withdraw.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a pending request of the caller's was deleted.</returns>
    /// <remarks>
    /// Deleted rather than marked: nothing reads the fact that somebody asked and changed their mind, and a
    /// tombstone would make "may these two be asked again" a query instead of a fact.
    /// </remarks>
    Task<bool> TryCancelRequestAsync(Guid senderId, Guid requestId, CancellationToken cancellationToken);

    /// <summary>Ends a friendship, from either side.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="friendId">The other party.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a friendship between the two was removed.</returns>
    /// <remarks>
    /// One row is deleted, so it ends for both sides at once — there is no state in which one of them still
    /// has a friend. Any request rows between the pair go with it, so they can befriend again later.
    /// </remarks>
    Task<bool> TryRemoveFriendAsync(Guid userId, Guid friendId, CancellationToken cancellationToken);

    /// <summary>Lists the caller's friends, newest first.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the caller's validator.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per friendship, projected to the other side.</returns>
    Task<IReadOnlyList<FriendSummary>> ListFriendsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Lists the caller's pending requests in one direction.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="direction">Incoming or outgoing.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the caller's validator.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per pending request.</returns>
    Task<IReadOnlyList<FriendRequestSummary>> ListRequestsAsync(
        Guid userId,
        FriendRequestDirection direction,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Whether two accounts are friends — what the `Friends` visibility tier will consult.</summary>
    /// <param name="userId">One account.</param>
    /// <param name="otherUserId">The other.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a friendship row exists for the pair.</returns>
    Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken);
}

/// <summary>What sending a request did, and the id it created.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="RequestId">The new request, or null when none was created.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as that method's return value, and
/// SPEC-012's Files table is the contract for what this change may create.
/// </remarks>
public sealed record SendFriendRequestResult(SendFriendRequestStatus Status, Guid? RequestId);

/// <summary>The three ways sending a request ends.</summary>
public enum SendFriendRequestStatus
{
    /// <summary>A pending request was created.</summary>
    Created = 0,

    /// <summary>
    /// Already friends, a request already pending in either direction, or the caller asking themselves. One
    /// outcome, because the endpoint answers `409` to all three and the caller knows which they attempted.
    /// </summary>
    Conflict = 1,

    /// <summary>No such account. `404`, and deliberately distinct from a conflict.</summary>
    RecipientNotFound = 2,
}
