using MediatR;

namespace MusicRoom.Application.Features.Auth.Commands.ExchangeAuthorizationCode;

/// <summary>
/// The authorization code grant — <c>POST /oauth2/token</c> with <c>grant_type=authorization_code</c>,
/// SPEC-044.
/// </summary>
/// <param name="Issuer">This provider's identifier, which is the origin the request arrived at. The ID
/// token's <c>iss</c>, passed in because <c>Application</c> does not read <c>HttpContext</c>.</param>
/// <param name="ClientId">The <c>client_id</c> parameter. Checked against the code's binding.</param>
/// <param name="RedirectUri">The <c>redirect_uri</c> parameter, which must be the one the code was issued
/// against — RFC 6749 §4.1.3.</param>
/// <param name="Code">The authorization code, in plaintext as the client received it.</param>
/// <param name="CodeVerifier">The PKCE verifier, checked against the stored challenge.</param>
public sealed record ExchangeAuthorizationCodeCommand(
    string Issuer,
    string ClientId,
    string RedirectUri,
    string Code,
    string CodeVerifier) : IRequest<ExchangeAuthorizationCodeResult>;
