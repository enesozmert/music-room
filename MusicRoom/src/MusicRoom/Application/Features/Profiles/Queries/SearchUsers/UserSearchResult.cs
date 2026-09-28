namespace MusicRoom.Application.Features.Profiles.Queries.SearchUsers;

/// <summary>
/// One account in a search result — `GET /api/v1/users`.
/// </summary>
/// <param name="UserId">The account id, which is what a friend request takes.</param>
/// <param name="DisplayName">The handle. The only always-public field, so the only one a search may show.</param>
/// <remarks>
/// Deliberately thin. The contract says search returns "public fields only", and `displayName` is the one field
/// that is always public (ADR-0034) — a profile field marked `Public` by its owner is still read through
/// `GET /users/{userId}`, where the projection is per field and per reader.
/// </remarks>
public sealed record UserSearchResult(Guid UserId, string DisplayName);
