using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The responses every credential endpoint shares.
/// </summary>
/// <remarks>
/// Extracted from <see cref="AuthEndpoints"/> when SPEC-007 pushed that file past the 300-line limit the
/// root .editorconfig enforces (S104), and its type coupling past 30 (S1200). One copy, because a second
/// copy of "what a throttled request answers" is a divergence waiting to happen.
/// </remarks>
internal static class CredentialEndpointResults
{
    /// <summary>
    /// Answers `429` with <c>Retry-After</c> when the per-account limiter refused the attempt.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <param name="exception">What the limiter reported.</param>
    /// <returns>The bodiless `429`.</returns>
    /// <remarks>
    /// The per-IP half of the limit is middleware and never reaches a handler; this is the half that knows
    /// which account was named (SPEC-006 escalation 2, option B). The body is empty for the same reason
    /// the middleware's is: a message saying which key tripped tells a caller which key to vary.
    /// </remarks>
    internal static IResult Throttled(HttpContext httpContext, CredentialAttemptThrottledException exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(exception.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Turns validation failures into the ProblemDetails error dictionary. Only rule messages cross this
    /// boundary — never the submitted value, which for some of these fields is a password.
    /// </summary>
    /// <param name="exception">The failure MediatR's validation behaviour threw.</param>
    /// <returns>The dictionary a ProblemDetails response carries.</returns>
    internal static Dictionary<string, string[]> ToErrors(ValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(ErrorMessage).ToArray(),
                StringComparer.Ordinal);
    }

    private static string ErrorMessage(ValidationFailure failure) => failure.ErrorMessage;
}
