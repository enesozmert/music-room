using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Auth.Commands.LinkSocialAccount;
using MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;
using MusicRoom.Application.Features.Auth.Commands.UnlinkSocialAccount;
using MusicRoom.Application.Features.Auth.Queries.ListSocialLinks;
using MusicRoom.Application.Validation;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Sign in with a provider, and manage the identities bound to an account — subject V.1, SPEC-041.
/// </summary>
/// <remarks>
/// Its own class, as `PasswordResetEndpoints` and `SessionEndpoints` are: `AuthEndpoints.cs` is one line under
/// the 300 the root .editorconfig allows, so four more routes could not live there.
/// <para>
/// The sign-in route is anonymous and rate-limited like every other credential route; the three link routes
/// require a token and act on the caller's own account, never on an account named in a request.
/// </para>
/// </remarks>
internal static class SocialAuthEndpoints
{
    /// <summary>Maps the four routes `docs/api/ENDPOINTS.md` §1 publishes.</summary>
    /// <param name="group">The `/api/v1/auth` group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        MapSocialSignIn(group);
        MapList(group);
        MapLink(group);
        MapUnlink(group);
    }

    private static void MapSocialSignIn(RouteGroupBuilder group) =>
        group.MapPost("/sessions/social", SignInAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Sessions)
            .WithName("PostAuthSocialSession")
            .WithSummary("Sign in with a provider")
            .WithDescription(
                "Exchanges an authorization code for a token pair. The code is exchanged by this API "
                + "with the client secret, never by the client — ADR-0008. An account is created the "
                + "first time an identity signs in, and it has no password until the owner sets one "
                + "through the reset flow.")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<TokenPairResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/social-links", ListAsync)
            .RequireAuthorization()
            .WithName("GetAuthSocialLinks")
            .WithSummary("List linked providers")
            .WithDescription("The caller's own links, oldest first. No route reads anybody else's.")
            .Produces<IReadOnlyList<SocialLinkResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapLink(RouteGroupBuilder group) =>
        group.MapPost("/social-links", LinkAsync)
            .RequireAuthorization()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Sessions)
            .WithName("PostAuthSocialLink")
            .WithSummary("Link a provider to this account")
            .WithDescription(
                "The same acquisition as signing in, deliberately: linking an identity the caller "
                + "cannot prove they hold would let anyone attach their provider account to somebody "
                + "else's.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapUnlink(RouteGroupBuilder group) =>
        group.MapDelete("/social-links/{provider}", UnlinkAsync)
            .RequireAuthorization()
            .WithName("DeleteAuthSocialLink")
            .WithSummary("Unlink a provider")
            .WithDescription(
                "Refused with 409 when it is the account's only way in: an account with no password "
                + "and one link would become one nobody can reach, including its owner.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

    private static async Task<IResult> SignInAsync(
        SocialSessionRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        SignInWithSocialCommand command = new(
            request.Provider,
            request.Code,
            request.CodeVerifier,
            request.RedirectUri);

        SignInWithSocialResult result;

        try
        {
            result = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }

        return result.Outcome switch
        {
            SignInWithSocialOutcome.SignedIn when result.Pair is { } pair => Results.Ok(
                new TokenPairResponse(pair.AccessToken, pair.RefreshToken, pair.ExpiresInSeconds)),

            // A provider nobody configured is a client mistake, and saying so costs nothing: the set of
            // providers this API supports is published in its own documentation.
            SignInWithSocialOutcome.UnknownProvider => CredentialEndpointResults.Refused(
                ValidationCodes.ProviderUnknown,
                "Unknown provider",
                "No provider is configured under that name."),

            // The provider authenticated somebody and asserted an address it has not verified. Said
            // plainly, because the caller can fix it at the provider and because it reveals nothing about
            // this API's accounts — ADR-0008's account-takeover note.
            SignInWithSocialOutcome.EmailUnverified => CredentialEndpointResults.Refused(
                ValidationCodes.SocialEmailUnverified,
                "Address not verified",
                "The provider has not verified the address on that identity."),

            // Everything else is one bodiless 401, as password sign-in is: which check failed is in the
            // log, and a caller who learns it learns how to pass it.
            _ => Results.Unauthorized(),
        };
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken cancellationToken)
    {
        IReadOnlyList<SocialLinkSummary> links = await sender.Send(
            new ListSocialLinksQuery(),
            cancellationToken);

        return Results.Ok(links
            .Select(link => new SocialLinkResponse(link.Provider, link.Email, link.LinkedAtUtc))
            .ToArray());
    }

    private static async Task<IResult> LinkAsync(
        SocialLinkRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        LinkSocialAccountCommand command = new(
            request.Provider,
            request.Code,
            request.CodeVerifier,
            request.RedirectUri);

        SocialLinkResult result;

        try
        {
            result = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }

        return result.Outcome switch
        {
            SocialLinkAttempt.Linked => Results.Created($"/api/v1/auth/social-links/{result.Provider}", null),

            SocialLinkAttempt.AlreadyLinked => CredentialEndpointResults.Conflict(
                ValidationCodes.SocialAlreadyLinked,
                "Already linked",
                "That identity is already linked to an account."),

            SocialLinkAttempt.UnknownProvider => CredentialEndpointResults.Refused(
                ValidationCodes.ProviderUnknown,
                "Unknown provider",
                "No provider is configured under that name."),

            _ => Results.Unauthorized(),
        };
    }

    private static async Task<IResult> UnlinkAsync(
        string provider,
        ISender sender,
        CancellationToken cancellationToken)
    {
        SocialUnlinkOutcome outcome = await sender.Send(
            new UnlinkSocialAccountCommand(provider),
            cancellationToken);

        return outcome switch
        {
            SocialUnlinkOutcome.Unlinked => Results.NoContent(),

            // Not 404: whether a provider is linked is the caller's own business and they are asking about
            // their own account, so the honest answer is that there was nothing to remove.
            SocialUnlinkOutcome.NotLinked => Results.NoContent(),

            _ => CredentialEndpointResults.Conflict(
                ValidationCodes.SocialLastSignInMethod,
                "Last sign-in method",
                "Set a password before unlinking the only provider on this account."),
        };
    }
}

/// <summary>The social sign-in payload — `docs/api/ENDPOINTS.md` §1.</summary>
/// <param name="Provider">Which provider, lower case.</param>
/// <param name="Code">The authorization code.</param>
/// <param name="CodeVerifier">The PKCE verifier.</param>
/// <param name="RedirectUri">The redirect URI the code was issued against.</param>
internal sealed record SocialSessionRequest(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri);

/// <summary>The link payload, which is the sign-in payload — SPEC-041.</summary>
/// <param name="Provider">Which provider, lower case.</param>
/// <param name="Code">The authorization code.</param>
/// <param name="CodeVerifier">The PKCE verifier.</param>
/// <param name="RedirectUri">The redirect URI the code was issued against.</param>
internal sealed record SocialLinkRequest(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri);

/// <summary>One linked provider, as its owner sees it.</summary>
/// <param name="Provider">Which provider.</param>
/// <param name="Email">The address it asserted when the link was made, or null.</param>
/// <param name="LinkedAtUtc">When it was linked.</param>
internal sealed record SocialLinkResponse(string Provider, string? Email, DateTime LinkedAtUtc);
