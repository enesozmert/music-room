using System.Text.Json.Serialization;
using MediatR;
using MusicRoom.Application.Features.Auth.Commands.RevokeToken;
using MusicRoom.Application.Features.Auth.Queries.GetUserInfo;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// UserInfo and revocation — the last third of the provider surface, ADR-0031, SPEC-045.
/// </summary>
/// <remarks>
/// **UserInfo answers the claims the session's grant allows.** The scopes are read from the session the
/// access token names, not from the request: a scope a caller could send is a scope a caller grants itself.
/// A session from the credential endpoints has no granted scope and gets the full set — it is the person's
/// own account, and `GET /me` already returns strictly more.
/// <para>
/// **Revocation refuses to lie.** An unknown token answers `200`, because RFC 7009 §2.2 says a caller must
/// not be able to tell a live token from a dead one. But `token_type_hint=access_token` answers `400
/// unsupported_token_type`: a 15-minute HMAC JWT cannot be revoked by this design, and `200` would tell a
/// client its access token is dead while it still opens every route.
/// </para>
/// </remarks>
internal static class OAuth2UserInfoEndpoints
{
    /// <summary>Where UserInfo lives.</summary>
    internal const string UserInfoPath = "/oauth2/userinfo";

    /// <summary>Where revocation lives.</summary>
    internal const string RevocationPath = "/oauth2/revoke";

    /// <summary>Maps both routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapUserInfoAndRevocation(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(UserInfoPath, UserInfoAsync)
            .RequireAuthorization()
            .WithName("GetOAuth2UserInfo")
            .WithSummary("UserInfo endpoint")
            .WithDescription(
                "The claims the session's grant allows — OpenID Connect Core 1.0 §5.3. `sub` always; "
                + "`email` and `email_verified` when the `email` scope was granted; `name` when `profile` "
                + "was. A session opened by the credential endpoints granted no scope and receives all of "
                + "them, because the caller is the account's own client. The subject comes from the "
                + "validated token and never from a parameter.")
            .Produces<UserInfoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        endpoints.MapPost(RevocationPath, RevokeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Tokens)
            .WithName("PostOAuth2Revoke")
            .WithSummary("Revocation endpoint")
            .WithDescription(
                "Form-encoded `token`, with an optional `token_type_hint` — RFC 7009. Revoking a refresh "
                + "token ends its whole session: every token descended from the same sign-in stops "
                + "rotating. An unknown or already-revoked token answers 200 as well, so the endpoint is "
                + "not an oracle for which tokens exist. `token_type_hint=access_token` answers 400 "
                + "`unsupported_token_type`, because a short-lived signed access token is not revocable "
                + "here and a 200 would say otherwise.")
            .Produces(StatusCodes.Status200OK)
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> UserInfoAsync(ISender sender, CancellationToken cancellationToken)
    {
        UserInfoClaims? claims = await sender.Send(new GetUserInfoQuery(), cancellationToken);

        // The account behind a valid token is gone: deleted while this token was still alive. Not 404 — the
        // question was "who is this token for", and the answer is that there is no longer anybody.
        return claims is null
            ? Results.Unauthorized()
            : Results.Ok(new UserInfoResponse(
                claims.Subject,
                claims.Email,
                claims.IsEmailVerified,
                claims.Name));
    }

    private static async Task<IResult> RevokeAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.HasFormContentType)
        {
            return Results.Json(
                new OAuthErrorResponse("invalid_request"),
                statusCode: StatusCodes.Status400BadRequest);
        }

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken);

        if (form["token_type_hint"].ToString() == "access_token")
        {
            return Results.Json(
                new OAuthErrorResponse("unsupported_token_type"),
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The outcome is read and dropped on purpose: both values answer 200, and the difference between
        // them is in the audit record — RFC 7009 §2.2.
        _ = await sender.Send(new RevokeTokenCommand(form["token"].ToString()), cancellationToken);

        return Results.Ok();
    }
}

/// <summary>
/// The UserInfo response — OpenID Connect Core 1.0 §5.1, with the members this provider can honour.
/// </summary>
/// <param name="Subject">The <c>sub</c>, which is the account's id.</param>
/// <param name="Email">The address, when `email` was granted.</param>
/// <param name="EmailVerified">Whether this API has proof of it, under the same scope.</param>
/// <param name="Name">The display name, when `profile` was granted.</param>
/// <remarks>
/// A claim the grant does not allow is **omitted**, not written as null: a member present with no value is a
/// member a client has to guess about. The names are the specification's, in snake case, which is why each
/// carries an explicit JSON name.
/// </remarks>
internal sealed record UserInfoResponse(
    [property: JsonPropertyName("sub")] string Subject,
    [property: JsonPropertyName("email")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Email,
    [property: JsonPropertyName("email_verified")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? EmailVerified,
    [property: JsonPropertyName("name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);
