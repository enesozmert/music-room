using MediatR;

namespace MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;

/// <summary>
/// Reads another account's profile, projected per field — `GET /api/v1/users/{userId}`.
/// </summary>
/// <param name="UserId">The account to read.</param>
/// <remarks>
/// Not auditable: a read changes nothing. The **reader** comes from the validated token; only the account being
/// read comes from the route, which is why there is no way to ask for a projection as somebody else.
/// </remarks>
public sealed record GetUserProfileQuery(Guid UserId) : IRequest<VisibleProfile>;
