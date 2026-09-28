using MediatR;

namespace MusicRoom.Application.Features.Profiles.Queries.SearchUsers;

/// <summary>
/// Searches accounts by display name — `GET /api/v1/users`.
/// </summary>
/// <param name="Query">The prefix to match, case-insensitively.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
/// <remarks>
/// The display name is the **only** field searched. Matching a profile field would let a stranger confirm a
/// friends-only value by probing for it — a leak with a search box in front of it (SPEC-013 decision 3).
/// </remarks>
public sealed record SearchUsersQuery(string Query, int Page, int PageSize)
    : IRequest<IReadOnlyList<UserSearchResult>>;
