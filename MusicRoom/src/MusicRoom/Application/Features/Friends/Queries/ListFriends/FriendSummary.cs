namespace MusicRoom.Application.Features.Friends.Queries.ListFriends;

/// <summary>
/// One friend, as the caller sees them — `GET /api/v1/me/friends`.
/// </summary>
/// <param name="UserId">The friend's account id.</param>
/// <param name="DisplayName">Their handle. Always public, so it needs no tier check — ADR-0034.</param>
/// <param name="FriendsSinceUtc">When the friendship was created.</param>
/// <remarks>
/// It carries no profile field. A friend list is a list of people, and what the caller may see **about** each
/// of them is the per-field projection of `GET /users/{userId}`, which is a different endpoint and a different
/// decision per field.
/// </remarks>
public sealed record FriendSummary(Guid UserId, string DisplayName, DateTime FriendsSinceUtc);
