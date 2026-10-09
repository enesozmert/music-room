using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MusicRoom.Application.Validation;

namespace MusicRoom.WebAPI;

/// <summary>
/// The one answer to a failure nobody decided — issue O29, SPEC-039.
/// </summary>
/// <remarks>
/// `AddProblemDetails` was registered and `UseExceptionHandler` was never called, so the service produced
/// nothing: a validation failure answered a `422` with `errors` and `codes`, and an unexpected exception
/// answered a bare `500` with no body. One API, two envelopes, and a client that needs two parsers of which
/// the second reads nothing.
/// <para>
/// **The response says the code and nothing else.** No message, no exception type, no stack trace, no
/// identifier. Everything a caller could use to diagnose is something an attacker can use to map the system —
/// an Npgsql message names a column and a constraint, and a stack trace names every assembly in the path. The
/// person who needs the detail is reading the log.
/// </para>
/// <para>
/// **The log line carries what the response does not**, at `Error`, with the trace identifier that joins it to
/// the request. A failure nobody can diagnose is worse than a leaky one; the fix is to put the detail where
/// only an operator can read it, not to omit it.
/// </para>
/// <para>
/// Its own file because `Program.cs` sits at S104's 300-line cap, which is issue O24 — the third unit in a row
/// to answer that wall with a new file rather than a raised severity.
/// </para>
/// </remarks>
/// <param name="logger">Where the exception goes.</param>
internal sealed class UnexpectedExceptionHandler(ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>The title a caller sees. Deliberately says nothing about what happened.</summary>
    internal const string Title = "Unexpected error";

    private readonly ILogger<UnexpectedExceptionHandler> _logger = logger;

    /// <summary>Logs the exception and writes the one body every failure shares.</summary>
    /// <param name="httpContext">The request that failed.</param>
    /// <param name="exception">What it failed with. It reaches the log and never the response.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>True: this handler answers every exception, because the alternative is a bare `500`.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // The trace identifier is in the log and not in the body: a body is public, and a correlation
        // identifier a caller can quote is a correlation identifier an attacker can enumerate.
        _logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}, trace {TraceIdentifier}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Title = Title,
                Status = StatusCodes.Status500InternalServerError,
                Extensions = { ["code"] = ValidationCodes.InternalUnexpected },
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }
}
