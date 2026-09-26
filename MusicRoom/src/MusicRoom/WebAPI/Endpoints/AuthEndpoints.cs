using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MusicRoom.Application.Features.Auth.Commands.Register;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Identity endpoints under <c>/api/v1/auth</c>, as specified in <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>Maps the identity endpoints implemented so far.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/auth");

        MapRegistrations(group);
        MapEmailVerifications(group);

        return endpoints;
    }

    private static void MapRegistrations(RouteGroupBuilder group) =>
        group.MapPost("/registrations", RegisterAsync)
            .AllowAnonymous()
            .WithName("PostAuthRegistration")
            .WithSummary("Register an account")
            .WithDescription(
                "Accepts the registration and answers 202 whether or not the address is already "
                + "registered. A 409 here would tell an anonymous caller who holds an account; the "
                + "mailbox tells the real user what happened.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapEmailVerifications(RouteGroupBuilder group) =>
        group.MapPost("/email-verifications", VerifyEmailAsync)
            .AllowAnonymous()
            .WithName("PostAuthEmailVerification")
            .WithSummary("Verify an email address")
            .WithDescription(
                "Spends the token from the verification mail exactly once and marks the address "
                + "verified. The description says nothing about what separates 400 from 410 for a "
                + "given token, because that is the caller's business only when they hold it.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone);

    private static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        RegisterCommand command = new(request.Email, request.Password, request.DisplayName);

        try
        {
            await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmailAsync(
        EmailVerificationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        VerifyEmailCommand command = new(request.Token);

        VerifyEmailOutcome outcome;

        try
        {
            outcome = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException)
        {
            // 400, not the 422 registration answers. The contract row for this endpoint lists 400
            // and 410 only, and the failures carry no body: a malformed token must not be echoed,
            // and the rule that rejected it is a hint about the token's shape.
            return Results.BadRequest();
        }

        return outcome switch
        {
            VerifyEmailOutcome.Verified => Results.NoContent(),
            VerifyEmailOutcome.Expired => Results.StatusCode(StatusCodes.Status410Gone),

            // Unknown, malformed and already spent share this answer on purpose — SPEC-003, owner
            // decision 2026-09-26.
            _ => Results.BadRequest(),
        };
    }

    /// <summary>
    /// Turns validation failures into the ProblemDetails error dictionary. Only rule messages cross
    /// this boundary — never the submitted value, which for one of these fields is a password.
    /// </summary>
    private static Dictionary<string, string[]> ToErrors(ValidationException exception) =>
        exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(ErrorMessage).ToArray(),
                StringComparer.Ordinal);

    private static string ErrorMessage(ValidationFailure failure) => failure.ErrorMessage;
}

/// <summary>
/// The registration payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Email">The address to register.</param>
/// <param name="Password">The plaintext password, 12 to 64 characters and at most 72 UTF-8 bytes.</param>
/// <param name="DisplayName">The name other users see.</param>
internal sealed record RegistrationRequest(string Email, string Password, string DisplayName);

/// <summary>
/// The email verification payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Token">The token from the verification mail.</param>
internal sealed record EmailVerificationRequest(string Token);
