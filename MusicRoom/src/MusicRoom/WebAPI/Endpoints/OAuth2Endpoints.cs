using Microsoft.AspNetCore.WebUtilities;
using MediatR;
using MusicRoom.Application.Features.Auth.Commands.AuthorizeClient;
using MusicRoom.Application.Validation;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The authorization endpoint — <c>docs/api/ENDPOINTS.md</c>'s provider surface, ADR-0031, SPEC-044.
/// </summary>
/// <remarks>
/// **This endpoint requires a bearer token** — the owner's answer A of 2026-10-11. There is no login page, no
/// cookie session and no HTML here, so a person who is not already signed in has nowhere to sign in; the
/// client authenticates with the password or social endpoints first and then drives the code flow. What that
/// costs is written down rather than discovered: this is a **first-party** provider, and ADR-0031 carries the
/// sentence as an amendment.
/// <para>
/// **The parameter names are OpenID Connect's and are not ours to rename**, which is also why they are read
/// out of the query collection by name instead of being bound to a record: `response_type` and
/// `code_challenge_method` are not C# property names, and a binder attribute per parameter would be six
/// places for one contract to drift.
/// </para>
/// </remarks>
internal static class OAuth2Endpoints
{
    /// <summary>Where the authorization endpoint lives.</summary>
    internal const string AuthorizePath = "/oauth2/authorize";

    /// <summary>Maps the authorization endpoint.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapAuthorizeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(AuthorizePath, AuthorizeAsync)
            .RequireAuthorization()
            .WithName("GetOAuth2Authorize")
            .WithSummary("Authorization endpoint — authorization code flow with PKCE")
            .WithDescription(
                "Takes `response_type=code`, `client_id`, `redirect_uri`, `scope`, `state`, `nonce`, "
                + "`code_challenge` and `code_challenge_method=S256`, and answers 302 to the registered "
                + "redirect URI with a single-use code that expires in 60 seconds. It requires a bearer "
                + "access token: this provider has no login page, so the account signs in through the "
                + "credential endpoints first — SPEC-044, the owner's answer A. An unknown client or an "
                + "unregistered redirect URI answers 400 without redirecting, because sending an error to a "
                + "URI nobody registered is the open redirect the registry exists to prevent.")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        IQueryCollection query = httpContext.Request.Query;

        string redirectUri = query["redirect_uri"].ToString();
        string? state = query["state"].ToString() is { Length: > 0 } sent ? sent : null;

        AuthorizeClientResult result = await sender.Send(
            new AuthorizeClientCommand(
                query["client_id"].ToString(),
                redirectUri,
                query["response_type"].ToString(),
                query["scope"].ToString(),
                query["nonce"].ToString() is { Length: > 0 } nonce ? nonce : null,
                query["code_challenge"].ToString(),
                query["code_challenge_method"].ToString()),
            cancellationToken);

        return Answer(result, redirectUri, state);
    }

    /// <summary>
    /// Turns the decision into a redirect, or into the one refusal that must not redirect.
    /// </summary>
    /// <remarks>
    /// Two shapes, and which one is used is a security decision rather than a style one. A client this API
    /// recognises, with a URI it registered, gets everything — including errors — at that URI, because that
    /// is where a conforming client is waiting. A client or URI this API does not recognise gets a
    /// ProblemDetails `400` here, because the alternative is forwarding an attacker's parameters to an
    /// attacker's address with this API's name on the redirect — RFC 6749 §4.1.2.1.
    /// </remarks>
    private static IResult Answer(AuthorizeClientResult result, string redirectUri, string? state) =>
        result.Outcome switch
        {
            AuthorizeOutcome.Granted when result.Code is { } code =>
                Results.Redirect(Location(redirectUri, "code", code, state)),

            AuthorizeOutcome.UnknownClient => Refused(
                ValidationCodes.ClientUnknown,
                "Unknown client",
                "No client is registered under that identifier."),

            AuthorizeOutcome.UnregisteredRedirectUri => Refused(
                ValidationCodes.RedirectUriUnregistered,
                "Unregistered redirect URI",
                "That client has not registered that redirect URI."),

            AuthorizeOutcome.UnsupportedResponseType =>
                Results.Redirect(Location(redirectUri, "error", "unsupported_response_type", state)),

            // `invalid_request` covers an absent challenge, one of the wrong shape, and
            // `code_challenge_method=plain` — which is refused rather than supported, SPEC-044 decision 2.
            _ => Results.Redirect(Location(redirectUri, "error", "invalid_request", state)),
        };

    /// <summary>
    /// Builds the redirect, carrying `state` back untouched when the client sent one.
    /// </summary>
    /// <remarks>
    /// `state` is echoed on success and on failure. A client uses it to recognise its own request, which is
    /// how it detects a response it did not ask for; dropping it on the error path would leave exactly the
    /// case where that check matters most without it.
    /// </remarks>
    private static string Location(string redirectUri, string name, string value, string? state)
    {
        Dictionary<string, string?> parameters = new(StringComparer.Ordinal) { [name] = value };

        if (state is not null)
        {
            parameters["state"] = state;
        }

        return QueryHelpers.AddQueryString(redirectUri, parameters);
    }

    private static IResult Refused(string code, string title, string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: title,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });
}
