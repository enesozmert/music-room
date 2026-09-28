using MediatR;

namespace MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;

/// <summary>
/// Reads the caller's own profile — `GET /api/v1/me`.
/// </summary>
/// <remarks>
/// Not an <c>IAuditableCommand</c>: reading your own profile changes nothing, and a row per read would bury the
/// deliberate actions the audit log exists for.
/// <para>
/// It takes no parameter. The account comes from the validated token, never from the request — a query that
/// accepted a user id would be an authorisation bug with a nice signature.
/// </para>
/// </remarks>
public sealed record GetOwnProfileQuery : IRequest<OwnProfile?>;
