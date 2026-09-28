using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;
using MusicRoom.Application.Features.Friends.Queries.ListFriends;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core friends graph.
/// </summary>
/// <remarks>
/// Every write is arbitrated by the database — a unique index or a conditional <c>UPDATE</c> — for the reason
/// every other write in this codebase is: a read-then-write check in C# has a window in it, and two people
/// befriending each other at once is exactly the traffic that finds the window.
/// </remarks>
internal sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public FriendshipRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<SendFriendRequestResult> TrySendRequestAsync(
        Guid senderId,
        Guid recipientId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (senderId == recipientId)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.Conflict, RequestId: null);
        }

        bool recipientExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == recipientId, cancellationToken)
            .ConfigureAwait(false);

        if (!recipientExists)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.RecipientNotFound, RequestId: null);
        }

        if (await AreFriendsAsync(senderId, recipientId, cancellationToken).ConfigureAwait(false))
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.Conflict, RequestId: null);
        }

        // A pending request in either direction blocks a new one. The read is a courtesy that produces a clean
        // 409; the unique index below is what actually holds when two of these race.
        bool alreadyPending = await _context.FriendRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.Status == FriendRequestStatus.Pending
                    && ((request.SenderId == senderId && request.RecipientId == recipientId)
                        || (request.SenderId == recipientId && request.RecipientId == senderId)),
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyPending)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.Conflict, RequestId: null);
        }

        return await InsertRequestAsync(senderId, recipientId, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryRespondToRequestAsync(
        Guid recipientId,
        Guid requestId,
        bool accept,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // One conditional UPDATE decides who answered the request: pending, addressed to this caller, and not
        // yet answered. Everybody else is told so by the database under its own row lock.
        int answered = await _context.FriendRequests
            .Where(request => request.Id == requestId
                && request.RecipientId == recipientId
                && request.Status == FriendRequestStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        request => request.Status,
                        accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Declined)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context would
                    // have stamped is stamped here — SDD §8.3.
                    .SetProperty(request => request.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (answered == 0)
        {
            return false;
        }

        if (accept)
        {
            await InsertFriendshipAsync(requestId, nowUtc, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryCancelRequestAsync(
        Guid senderId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        // Keyed on the sender, so a recipient cannot cancel what they may decline.
        int deleted = await _context.FriendRequests
            .Where(request => request.Id == requestId
                && request.SenderId == senderId
                && request.Status == FriendRequestStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryRemoveFriendAsync(
        Guid userId,
        Guid friendId,
        CancellationToken cancellationToken)
    {
        if (userId == friendId)
        {
            return false;
        }

        (Guid userAId, Guid userBId) = Friendship.Pair(userId, friendId);

        int deleted = await _context.Friendships
            .Where(friendship => friendship.UserAId == userAId && friendship.UserBId == userBId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deleted == 0)
        {
            return false;
        }

        // The request rows go with it, so the pair can befriend again. A declined or accepted row left behind
        // would block the next request through the unique index.
        await _context.FriendRequests
            .Where(request =>
                (request.SenderId == userId && request.RecipientId == friendId)
                || (request.SenderId == friendId && request.RecipientId == userId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FriendSummary>> ListFriendsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await _context.Friendships
            .AsNoTracking()
            .Where(friendship => friendship.UserAId == userId || friendship.UserBId == userId)
            .OrderByDescending(friendship => friendship.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // Projected to the other side: the row is symmetric, so which column holds the friend depends on
            // which one holds the caller.
            .Select(friendship => new FriendSummary(
                friendship.UserAId == userId ? friendship.UserBId : friendship.UserAId,
                _context.Users
                    .Where(user => user.Id == (friendship.UserAId == userId
                        ? friendship.UserBId
                        : friendship.UserAId))
                    .Select(user => user.DisplayName)
                    .First(),
                friendship.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FriendRequestSummary>> ListRequestsAsync(
        Guid userId,
        FriendRequestDirection direction,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        bool incoming = direction == FriendRequestDirection.Incoming;

        return await _context.FriendRequests
            .AsNoTracking()
            .Where(request => request.Status == FriendRequestStatus.Pending
                && (incoming ? request.RecipientId == userId : request.SenderId == userId))
            .OrderByDescending(request => request.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(request => new FriendRequestSummary(
                request.Id,
                incoming ? request.SenderId : request.RecipientId,
                _context.Users
                    .Where(user => user.Id == (incoming ? request.SenderId : request.RecipientId))
                    .Select(user => user.DisplayName)
                    .First(),
                request.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> AreFriendsAsync(
        Guid userId,
        Guid otherUserId,
        CancellationToken cancellationToken)
    {
        if (userId == otherUserId)
        {
            return false;
        }

        (Guid userAId, Guid userBId) = Friendship.Pair(userId, otherUserId);

        return await _context.Friendships
            .AsNoTracking()
            .AnyAsync(
                friendship => friendship.UserAId == userAId && friendship.UserBId == userBId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the pending row. A duplicate is the unique index's answer, not a second row.
    /// </summary>
    private async Task<SendFriendRequestResult> InsertRequestAsync(
        Guid senderId,
        Guid recipientId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // A declined or accepted row for this exact direction would collide with the unique index, so it is
        // replaced: the pair is allowed to try again, and the checks above proved they may.
        await _context.FriendRequests
            .Where(request => request.SenderId == senderId && request.RecipientId == recipientId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        FriendRequest request = new()
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            RecipientId = recipientId,
            Status = FriendRequestStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.FriendRequests.Add(request);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SendFriendRequestResult(SendFriendRequestStatus.Created, request.Id);
    }

    /// <summary>
    /// Creates the friendship for an accepted request, in the transaction the caller already owns.
    /// </summary>
    /// <remarks>
    /// Reached only by the single caller whose conditional <c>UPDATE</c> won the request, so the pair is written
    /// once however many accepts raced — and the unique index is the second line of defence if that reasoning
    /// is ever wrong.
    /// </remarks>
    private async Task InsertFriendshipAsync(
        Guid requestId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var parties = await _context.FriendRequests
            .AsNoTracking()
            .Where(request => request.Id == requestId)
            .Select(request => new { request.SenderId, request.RecipientId })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        (Guid userAId, Guid userBId) = Friendship.Pair(parties.SenderId, parties.RecipientId);

        _context.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            UserAId = userAId,
            UserBId = userBId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
