using Serilog;

namespace MusicRoom.WebAPI;

/// <summary>
/// What happens around a request that fails — the error envelope and the request log, SPEC-039.
/// </summary>
/// <remarks>
/// One class for both because they are the same concern seen twice: the log line is what an operator reads
/// when a request fails, and the `ProblemDetails` body is what the caller reads. Issue O29 was the gap between
/// them — `AddProblemDetails` registered, `UseExceptionHandler` never called, so the caller read nothing.
/// <para>
/// Its own file rather than two more lines in the composition root, which sits at S104's 300-line cap: issue
/// O24, and the third unit in a row to answer that wall with a file rather than a raised severity. Moving the
/// request logger here too is what keeps the root the same length as before.
/// </para>
/// </remarks>
internal static class ErrorHandling
{
    /// <summary>Registers the problem-details service and the handler that uses it.</summary>
    /// <param name="services">The container being built.</param>
    internal static void Add(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
    }

    /// <summary>Puts the handler in front of everything, then the request logger.</summary>
    /// <param name="app">The application being built.</param>
    /// <remarks>
    /// The handler is **first**. One that ran later could not shape the response of an exception thrown ahead
    /// of it, and the position of the request logger is not a detail: a failure that escapes the handler is a
    /// bare `500` with no body, which is the defect O29 named.
    /// </remarks>
    internal static void Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();
    }
}
