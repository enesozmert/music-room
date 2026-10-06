using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Auth.Commands.RequestPasswordReset;
using MusicRoom.Application.Features.Auth.Commands.ResetPassword;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The password reset pair under <c>/api/v1/auth</c> — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <remarks>
/// Its own file rather than two more members on <see cref="AuthEndpoints"/>, which SPEC-007 pushed past
/// the 300-line and 30-type limits the root .editorconfig enforces.
/// </remarks>
internal static class PasswordResetEndpoints
{
    /// <summary>Maps both routes onto the identity group.</summary>
    /// <param name="group">The <c>/api/v1/auth</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapRequests(group);
        MapResets(group);
    }

    private static void MapRequests(RouteGroupBuilder group) =>
        group.MapPost("/password-reset-requests", RequestPasswordResetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.PasswordResetRequests)
            .WithName("PostAuthPasswordResetRequest")
            .WithSummary("Ask for a password reset link")
            .WithDescription(
                "Answers 202 for any address that is a valid address. Whether a mail follows is not "
                + "stated and cannot be inferred from the response: a distinct answer here would tell "
                + "an anonymous caller which addresses hold an account.")
            // 400 from the client-context middleware, not from this handler — SPEC-008, SDD §10.7.
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapResets(RouteGroupBuilder group) =>
        group.MapPost("/password-resets", ResetPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.PasswordResets)
            .WithName("PostAuthPasswordReset")
            .WithSummary("Replace a forgotten password")
            .WithDescription(
                "Spends the token from the reset mail exactly once, replaces the password and revokes "
                + "every refresh token the account holds, so a password change ends every session "
                + "opened with the old one. An access token issued moments earlier stays valid until it "
                + "expires; that window is the 15 minutes the session design fixes. A 422 describes the "
                + "new password only, never the token.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static async Task<IResult> RequestPasswordResetAsync(
        PasswordResetRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RequestPasswordResetCommand command = new(request.Email);

        CredentialThrottle? throttled;

        try
        {
            throttled = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            // 422 with the rule messages, as registration and resend answer: the rules describe the
            // shape of an address and say nothing about which addresses exist.
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return throttled is null
            ? Results.Accepted()
            : CredentialEndpointResults.Throttled(httpContext, throttled);
    }

    private static async Task<IResult> ResetPasswordAsync(
        PasswordResetSubmission request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        ResetPasswordCommand command = new(request.Token, request.NewPassword);

        ResetPasswordOutcome outcome;

        try
        {
            outcome = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return PasswordRuleFailure(exception);
        }

        return outcome switch
        {
            ResetPasswordOutcome.Reset => Results.NoContent(),
            ResetPasswordOutcome.Expired => Results.StatusCode(StatusCodes.Status410Gone),

            // The same shape a policy failure gets, keyed on the same property, because to a client this is one:
            // the password it sent is not acceptable. The token it sent is still spendable — SPEC-035.
            ResetPasswordOutcome.RefusedByContext => ContextRefusal(),

            // Unknown, malformed and already spent share this answer on purpose, exactly as email
            // verification does.
            _ => Results.BadRequest(),
        };
    }

    /// <summary>
    /// Answers a validation failure on the reset endpoint: 422 when it is about the new password, a
    /// bodiless 400 when it is about the token.
    /// </summary>
    /// <remarks>
    /// The caller needs to know that their new password broke the policy — that is their own input, and
    /// the rules are public. A message about the token would describe the shape of a credential, so the
    /// token failure is answered the way an unknown token is.
    /// </remarks>
    /// <summary>
    /// Answers the context screen's refusal in the shape a validation failure takes.
    /// </summary>
    /// <remarks>
    /// Built here rather than thrown from the handler: a `ValidationException` inside a handler would be rolled
    /// back together with the audit row recording the refusal, because the audit behaviour is fail-closed —
    /// SPEC-009's lesson, and SPEC-035's acceptance criterion. The key is the property a client already reads
    /// for this route, and the message never names the word that matched.
    /// </remarks>
    private static IResult ContextRefusal() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [nameof(ResetPasswordCommand.NewPassword)] = [PasswordRules.ContextMessage],
            },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult PasswordRuleFailure(ValidationException exception)
    {
        Dictionary<string, string[]> errors = CredentialEndpointResults.ToErrors(exception);

        return errors.ContainsKey(nameof(ResetPasswordCommand.Token))
            ? Results.BadRequest()
            : Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
}

/// <summary>
/// The reset request payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Email">The address to send a reset link to.</param>
internal sealed record PasswordResetRequest(string Email);

/// <summary>
/// The reset payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Token">The token from the reset mail.</param>
/// <param name="NewPassword">The replacement password. Never logged or echoed back.</param>
internal sealed record PasswordResetSubmission(string Token, string NewPassword);
