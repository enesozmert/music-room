using MediatR;

namespace MusicRoom.Application.Features.Friends.Queries.ListFriends;

/// <summary>
/// Lists the caller's friends — `GET /api/v1/me/friends`.
/// </summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows.</param>
/// <remarks>
/// Not auditable: reading your own friend list changes nothing. Paged because an unbounded list is OWASP API4,
/// and the account comes from the token rather than the query.
/// </remarks>
public sealed record ListFriendsQuery(int Page, int PageSize) : IRequest<IReadOnlyList<FriendSummary>>;
