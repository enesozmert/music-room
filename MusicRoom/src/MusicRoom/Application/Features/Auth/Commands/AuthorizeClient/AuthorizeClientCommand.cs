using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.AuthorizeClient;

/// <summary>
/// An authenticated account authorising a client — <c>GET /oauth2/authorize</c>, SPEC-044.
/// </summary>
/// <param name="ClientId">The <c>client_id</c> parameter, as sent.</param>
/// <param name="RedirectUri">The <c>redirect_uri</c> parameter, compared against the registered list.</param>
/// <param name="ResponseType">The <c>response_type</c> parameter. Only <c>code</c> is supported.</param>
/// <param name="Scope">The <c>scope</c> parameter, or null. Absent means <c>openid</c>.</param>
/// <param name="Nonce">The <c>nonce</c> parameter, or null. Echoed into the ID token when present.</param>
/// <param name="CodeChallenge">The <c>code_challenge</c> parameter. Required — this provider has no
/// flow without PKCE.</param>
/// <param name="CodeChallengeMethod">The <c>code_challenge_method</c> parameter. <c>S256</c> only.</param>
/// <remarks>
/// The account is not a parameter: it comes from the validated bearer token through
/// <c>ICurrentUserAccessor</c>, which is the owner's answer A. A command that named the account would be a
/// command that could be sent on somebody else's behalf.
/// </remarks>
public sealed record AuthorizeClientCommand(
    string ClientId,
    string RedirectUri,
    string ResponseType,
    string? Scope,
    string? Nonce,
    string CodeChallenge,
    string CodeChallengeMethod) : IRequest<AuthorizeClientResult>;
