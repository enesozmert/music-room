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
    /// <param name="throttle">What the limiter reported.</param>
    /// <returns>The bodiless `429`.</returns>
    /// <remarks>
    /// The per-IP half of the limit is middleware and never reaches a handler; this is the half that knows
    /// which account was named (SPEC-006 escalation 2, option B). The body is empty for the same reason
    /// the middleware's is: a message saying which key tripped tells a caller which key to vary.
    /// <para>
    /// The refusal arrives as a value rather than an exception since SPEC-009, so that the audit record for a
    /// throttled attempt commits on the pipeline's ordinary path instead of being rolled back.
    /// </para>
    /// </remarks>
    internal static IResult Throttled(HttpContext httpContext, CredentialThrottle throttle)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(throttle);

        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(throttle.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

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

    /// <summary>
    /// The whole `422` answer for a validation failure: the messages, and the codes beside them — ADR-0045.
    /// </summary>
    /// <param name="exception">The failure MediatR's validation behaviour threw.</param>
    /// <returns>A `422` carrying `errors` and the `codes` extension.</returns>
    /// <remarks>
    /// **One function, one pass, two dictionaries.** They are grouped from the same failure list in the same
    /// order, so `errors["newPassword"][0]` and `codes["newPassword"][0]` describe the same refusal. Two
    /// builders in two places would disagree the first time one was edited, which is the reason ADR-0045 names
    /// this constraint rather than leaving it to care.
    /// </remarks>
    internal static IResult Invalid(ValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        List<IGrouping<string, ValidationFailure>> grouped = [.. exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)];

        Dictionary<string, string[]> errors = grouped.ToDictionary(
            group => group.Key,
            group => group.Select(ErrorMessage).ToArray(),
            StringComparer.Ordinal);

        Dictionary<string, string[]> codes = grouped.ToDictionary(
            group => group.Key,
            group => group.Select(failure => failure.ErrorCode).ToArray(),
            StringComparer.Ordinal);

        return Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["codes"] = codes });
    }

    /// <summary>
    /// A refusal a handler decided, which has no field to key on — ADR-0045.
    /// </summary>
    /// <param name="code">The code from <see cref="MusicRoom.Application.Validation.ValidationCodes"/>.</param>
    /// <param name="title">The short English title.</param>
    /// <param name="detail">The English detail, for a developer rather than an end user.</param>
    /// <returns>A `422` carrying the single `code` extension.</returns>
    internal static IResult Refused(string code, string title, string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: title,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

    /// <summary>
    /// A `409` carrying the same single `code` member a `422` refusal does — ADR-0045.
    /// </summary>
    /// <param name="code">The code from <see cref="MusicRoom.Application.Validation.ValidationCodes"/>.</param>
    /// <param name="title">The short English title.</param>
    /// <param name="detail">The English detail, for a developer rather than an end user.</param>
    /// <returns>A `409` a client can switch on.</returns>
    internal static IResult Conflict(string code, string title, string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            title: title,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });

    private static string ErrorMessage(ValidationFailure failure) => failure.ErrorMessage;
}
