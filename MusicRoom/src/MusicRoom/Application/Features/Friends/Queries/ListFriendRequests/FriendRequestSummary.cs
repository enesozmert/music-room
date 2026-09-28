namespace MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;

/// <summary>
/// One pending friend request, in whichever direction was asked for.
/// </summary>
/// <param name="RequestId">The id the respond, decline and cancel routes take.</param>
/// <param name="UserId">The other party: the sender on an incoming request, the recipient on an outgoing one.</param>
/// <param name="DisplayName">Their handle.</param>
/// <param name="RequestedAtUtc">When it was sent.</param>
public sealed record FriendRequestSummary(
    Guid RequestId,
    Guid UserId,
    string DisplayName,
    DateTime RequestedAtUtc);

/// <summary>
/// Which way a listed request points, relative to the caller.
/// </summary>
/// <remarks>
/// The contract's query parameter is `direction=incoming|outgoing`, and it is required: a combined list would
/// need a per-row flag the client then branches on, and both screens that show these lists show one direction.
/// </remarks>
public enum FriendRequestDirection
{
    /// <summary>Somebody asked the caller.</summary>
    Incoming = 0,

    /// <summary>The caller asked somebody.</summary>
    Outgoing = 1,
}
