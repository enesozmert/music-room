using MediatR;

namespace MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;

/// <summary>
/// Lists the caller's pending requests in one direction — `GET /api/v1/me/friend-requests`.
/// </summary>
/// <param name="Direction">Incoming or outgoing. Required by the contract.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows.</param>
public sealed record ListFriendRequestsQuery(FriendRequestDirection Direction, int Page, int PageSize)
    : IRequest<IReadOnlyList<FriendRequestSummary>>;
