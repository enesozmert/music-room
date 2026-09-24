using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MusicRoom.Application.Features.Auth.Commands.Register;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Identity endpoints under <c>/api/v1/auth</c>, as specified in <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>Maps <c>POST /api/v1/auth/registrations</c>.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/auth");

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

        return endpoints;
    }

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
