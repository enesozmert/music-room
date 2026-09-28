namespace MusicRoom.Application.Abstractions.Auditing;

/// <summary>
/// What this layer may learn about the caller's client — SDD §10.7.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>WebAPI</c>, which is where <c>HttpContext</c> lives. That is not
/// an outward reference: the composition root supplies the implementation, and moving it to
/// <c>Infrastructure</c> would make that project depend on the web stack to read three strings.
/// </remarks>
public interface IClientContextAccessor
{
    /// <summary>
    /// The current request's client context, or null when there is no request — a background service, or
    /// a command sent from a test without one.
    /// </summary>
    ClientContext? Current { get; }
}

/// <summary>
/// The three values subject V.6 requires on every audited action, plus the trace id that joins a record
/// to the application log.
/// </summary>
/// <param name="Platform">From <c>X-Client-Platform</c>.</param>
/// <param name="Device">From <c>X-Client-Device</c>.</param>
/// <param name="AppVersion">From <c>X-Client-App-Version</c>.</param>
/// <param name="CorrelationId">The request's trace id — ADR-0022.</param>
/// <remarks>
/// Declared beside the accessor rather than in its own file: it exists only as that property's type, and
/// SPEC-008's Files table is the contract for what this change may create.
/// </remarks>
public sealed record ClientContext(string Platform, string Device, string AppVersion, string CorrelationId);
