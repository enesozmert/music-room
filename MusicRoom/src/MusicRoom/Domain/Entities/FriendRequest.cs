using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One person asking another to be friends — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <remarks>
/// A request is the only way a <see cref="Friendship"/> comes into existence, so the graph can never gain an
/// edge nobody agreed to. A pending request in **either** direction blocks a new one, which is what stops two
/// people asking each other at once from producing two friendships.
/// </remarks>
public sealed class FriendRequest : Entity
{
    /// <summary>The account that asked.</summary>
    public Guid SenderId { get; set; }

    /// <summary>The account that was asked.</summary>
    public Guid RecipientId { get; set; }

    /// <summary>Where the request stands.</summary>
    public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;
}

/// <summary>
/// The three states a friend request can be in.
/// </summary>
/// <remarks>
/// There is no `Cancelled`: a withdrawn request is deleted, because nothing reads the fact that somebody once
/// asked and changed their mind, and keeping it would make "may these two be asked again" a query instead of a
/// fact. Declining keeps the row, so the decliner is not asked again immediately by the same sender.
/// </remarks>
public enum FriendRequestStatus
{
    /// <summary>Waiting for the recipient.</summary>
    Pending = 0,

    /// <summary>Accepted; a friendship exists.</summary>
    Accepted = 1,

    /// <summary>Declined; no friendship, and the row stays.</summary>
    Declined = 2,
}
