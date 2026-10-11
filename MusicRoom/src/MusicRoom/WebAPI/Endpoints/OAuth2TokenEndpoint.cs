using System.Text.Json.Serialization;
using MediatR;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Auth.Commands.ExchangeAuthorizationCode;
using MusicRoom.Application.Features.Auth.Commands.RotateSession;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The token endpoint — <c>docs/api/ENDPOINTS.md</c>'s provider surface, ADR-0031, SPEC-044.
/// </summary>
/// <remarks>
/// **Two grants.** `authorization_code` spends a code from the authorization endpoint and answers with an
/// access token, an ID token and a refresh token. `refresh_token` rotates a session, and reuses S1's rotation
/// rather than growing a second refresh mechanism — ADR-0032's family revocation is the session-theft
/// detector, and two refresh paths would mean one of them eventually stops revoking.
/// <para>
/// **The refresh grant answers no ID token.** An ID token asserts an authentication event, and a refresh
/// exchange is not one: nobody proved anything to this API, a stored token was presented. OpenID Connect
/// Core 1.0 §12.1 leaves it optional, and the honest reading of "optional" here is to leave out an assertion
/// nothing new supports.
/// </para>
/// <para>
/// **The request is form-encoded and the error body is not ProblemDetails.** Both are fixed by RFC 6749 §4.1
/// and §5.2. This is the one place in the API where a refusal is `{"error":"invalid_grant"}` rather than
/// ADR-0045's `code`, because the specification already named these errors and a client's library reads them.
/// </para>
/// <para>
/// Its own file beside <see cref="OAuth2Endpoints"/>: the root .editorconfig caps a file at 300 lines (S104),
/// and the two endpoints document two different contracts.
/// </para>
/// </remarks>
internal static class OAuth2TokenEndpoint
{
    /// <summary>Where the token endpoint lives.</summary>
    internal const string TokenPath = "/oauth2/token";

    /// <summary>The only token type this endpoint issues.</summary>
    internal const string BearerTokenType = "Bearer";

    /// <summary>Maps the token endpoint.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(TokenPath, ExchangeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Tokens)
            .WithName("PostOAuth2Token")
            .WithSummary("Token endpoint — code exchange and refresh")
            .WithDescription(
                "Form-encoded, as RFC 6749 requires. With `grant_type=authorization_code` it takes `code`, "
                + "`code_verifier`, `client_id` and `redirect_uri`, spends the code once, and answers an "
                + "access token, an ID token and a refresh token. With `grant_type=refresh_token` it takes "
                + "`refresh_token` and rotates the session, which revokes the whole family if a spent token "
                + "is presented. There is no `grant_type=password`: OAuth 2.1 removes it. Errors are RFC "
                + "6749 §5.2 objects, not ProblemDetails.")
            .Produces<TokenGrantResponse>(StatusCodes.Status200OK)
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<OAuthErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasFormContentType)
        {
            return Error("invalid_request", StatusCodes.Status400BadRequest);
        }

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);

        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await CodeGrantAsync(httpContext, form, sender, cancellationToken),
            "refresh_token" => await RefreshGrantAsync(form, sender, cancellationToken),

            // Includes `password`, which is absent on purpose: OAuth 2.1 removes the resource-owner
            // password-credentials grant and RFC 9700 deprecates it — ADR-0031.
            _ => Error("unsupported_grant_type", StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> CodeGrantAsync(
        HttpContext httpContext,
        IFormCollection form,
        ISender sender,
        CancellationToken cancellationToken)
    {
        ExchangeAuthorizationCodeResult result = await sender.Send(
            new ExchangeAuthorizationCodeCommand(
                Issuer(httpContext),
                form["client_id"].ToString(),
                form["redirect_uri"].ToString(),
                form["code"].ToString(),
                form["code_verifier"].ToString()),
            cancellationToken);

        return result.Outcome switch
        {
            TokenGrantOutcome.Granted when result.Pair is { } pair => Results.Ok(
                new TokenGrantResponse(
                    pair.AccessToken,
                    BearerTokenType,
                    pair.ExpiresInSeconds,
                    pair.RefreshToken,
                    result.IdToken,
                    result.Scope)),

            // `invalid_client` is a 401 because the client failed to identify itself, which RFC 6749 §5.2
            // distinguishes from a grant that was simply refused.
            TokenGrantOutcome.InvalidClient => Error("invalid_client", StatusCodes.Status401Unauthorized),

            _ => Error("invalid_grant", StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// The refresh grant, which is S1's rotation under OAuth's parameter names.
    /// </summary>
    /// <remarks>
    /// No client check: the refresh token is the credential, it is bound to the session that issued it, and a
    /// public client holds no secret to prove anything else with — ADR-0031's "the mobile client is a public
    /// client". Presenting a spent one revokes the family, inside the existing handler.
    /// </remarks>
    private static async Task<IResult> RefreshGrantAsync(
        IFormCollection form,
        ISender sender,
        CancellationToken cancellationToken)
    {
        AuthTokenPair? pair = await sender.Send(
            new RotateSessionCommand(form["refresh_token"].ToString()),
            cancellationToken);

        return pair is null
            ? Error("invalid_grant", StatusCodes.Status400BadRequest)
            : Results.Ok(new TokenGrantResponse(
                pair.AccessToken,
                BearerTokenType,
                pair.ExpiresInSeconds,
                pair.RefreshToken,
                null,
                null));
    }

    /// <summary>
    /// The issuer, which is this host's own origin — the same value the discovery document publishes.
    /// </summary>
    /// <remarks>
    /// Read from the request for the reason `OpenIdMetadataEndpoints` records: an `iss` that does not match
    /// the host the discovery document was fetched from is refused by a conforming client.
    /// </remarks>
    private static string Issuer(HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

    private static IResult Error(string error, int statusCode) =>
        Results.Json(new OAuthErrorResponse(error), statusCode: statusCode);
}

/// <summary>
/// The successful token response — RFC 6749 §5.1, with OpenID Connect's `id_token`.
/// </summary>
/// <param name="AccessToken">The bearer token every other endpoint takes.</param>
/// <param name="TokenType">`Bearer`, and nothing else.</param>
/// <param name="ExpiresIn">The access token's lifetime in seconds, for the client's own planning.</param>
/// <param name="RefreshToken">The opaque refresh token. Rotated on every use.</param>
/// <param name="IdToken">The RS256 assertion of who authenticated, on the code grant only.</param>
/// <param name="Scope">What was granted, on the code grant only.</param>
/// <remarks>
/// The member names are the specification's, in snake case, which is why each one carries an explicit JSON
/// name rather than relying on this API's own naming policy.
/// </remarks>
internal sealed record TokenGrantResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("id_token")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdToken,
    [property: JsonPropertyName("scope")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Scope);

/// <summary>
/// A refused grant — RFC 6749 §5.2.
/// </summary>
/// <param name="Error">The specification's code: `invalid_request`, `invalid_client`, `invalid_grant` or
/// `unsupported_grant_type`.</param>
/// <remarks>
/// No `error_description`. Which of the six causes of `invalid_grant` applied is in the log, and a caller who
/// learns it learns how to tell a live code from a spent one — the same reasoning the bodiless `401` on
/// sign-in follows.
/// </remarks>
internal sealed record OAuthErrorResponse([property: JsonPropertyName("error")] string Error);
