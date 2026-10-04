using System.Globalization;
using FluentValidation;
using MediatR;
using MusicRoom.Application.Features.Auth.Commands.ChangePassword;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Changing the caller's own password — `POST /api/v1/me/password`, ENDPOINTS §1, SPEC-028.
/// </summary>
/// <remarks>
/// Its own file rather than another route in <see cref="AuthEndpoints"/>: that file is near the 300-line gate,
/// and this route is about the account rather than about a session.
/// <para>
/// A wrong current password is `422` and not `401`. The token is valid and the request is authenticated; what
/// failed is a precondition of the operation, and a `401` would tell a well-behaved client its session had
/// ended and send it into a refresh-then-retry loop over a typo.
/// </para>
/// </remarks>
public static class AccountPasswordEndpoints
{
    /// <summary>What a caller is told when the current password does not match.</summary>
    public const string RefusedDetail = "The current password is incorrect.";

    /// <summary>Maps the route.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapAccountPasswordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/me/password", ChangeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(CredentialRateLimiterPolicies.PasswordChanges)
            .WithName("PostPasswordChange")
            .WithSummary("Change the caller's password")
            .WithDescription(
                "Takes the current password as proof and replaces it. Every other session the account holds "
                + "is revoked; the caller's own stays alive, because it is the session that just proved it "
                + "holds the new credential. A wrong current password is 422, not 401: the token is fine.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ChangeAsync(
        ChangePasswordCommand command,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(httpContext);

        try
        {
            ChangePasswordResult result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                ChangePasswordOutcome.Changed => Results.NoContent(),
                ChangePasswordOutcome.Throttled => Throttled(httpContext, result.RetryAfter),
                _ => Results.Problem(
                    detail: RefusedDetail,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Password not changed"),
            };
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    /// <summary>
    /// The bodiless `429`, with the wait the per-account limiter reported.
    /// </summary>
    /// <remarks>
    /// Not <see cref="CredentialEndpointResults.Throttled"/>: that helper takes the `CredentialThrottle` the
    /// anonymous credential endpoints return, and this command carries its wait on its own result. The response
    /// is the same shape, which is what matters to a client.
    /// </remarks>
    private static IResult Throttled(HttpContext httpContext, TimeSpan retryAfter)
    {
        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
}
