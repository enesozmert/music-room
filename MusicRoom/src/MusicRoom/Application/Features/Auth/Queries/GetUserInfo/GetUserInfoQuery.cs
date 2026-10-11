using MediatR;

namespace MusicRoom.Application.Features.Auth.Queries.GetUserInfo;

/// <summary>
/// The claims a session's grant allows — <c>GET /oauth2/userinfo</c>, SPEC-045.
/// </summary>
/// <remarks>
/// No parameters, deliberately. The account comes from the validated token's <c>sub</c> and the session from
/// its <c>sid</c>, both through <c>ICurrentUserAccessor</c>; a query that named either would be a query
/// somebody could send about another account.
/// </remarks>
public sealed record GetUserInfoQuery : IRequest<UserInfoClaims?>;

/// <summary>
/// What UserInfo answers — OpenID Connect Core 1.0 §5.3 and §5.4.
/// </summary>
/// <param name="Subject">The <c>sub</c>, always present: it is what identifies the authentication.</param>
/// <param name="Email">The address, when <c>email</c> was granted. Null otherwise.</param>
/// <param name="IsEmailVerified">Whether this API has proof of the address, beside it and under the same
/// scope. Null when the address is absent, because `email_verified` without an address asserts nothing.</param>
/// <param name="Name">The display name, when <c>profile</c> was granted. Null otherwise.</param>
/// <remarks>
/// Null means "this scope was not granted", and the endpoint omits the member rather than writing a null:
/// a claim present with no value is a claim a client has to guess about.
/// </remarks>
public sealed record UserInfoClaims(string Subject, string? Email, bool? IsEmailVerified, string? Name);
