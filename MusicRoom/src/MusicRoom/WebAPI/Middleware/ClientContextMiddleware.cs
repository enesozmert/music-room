using System.Diagnostics;

namespace MusicRoom.WebAPI.Middleware;

/// <summary>
/// Enforces the mandatory client headers on every mutating request — SDD §10.7 steps 1 to 3.
/// </summary>
/// <remarks>
/// Subject V.6 requires platform, device and app version on every client action, so a mutation without
/// them is refused rather than defaulted: a silent default would void the requirement while appearing to
/// satisfy it. A read is untouched, and so are the health probes — a probe is not a client action.
/// <para>
/// It runs after the rate limiter, so a request the limiter would refuse never reaches this check.
/// </para>
/// </remarks>
public sealed class ClientContextMiddleware
{
    /// <summary>The header carrying the client platform.</summary>
    public const string PlatformHeader = "X-Client-Platform";

    /// <summary>The header carrying the client device.</summary>
    public const string DeviceHeader = "X-Client-Device";

    /// <summary>The header carrying the client application version.</summary>
    public const string AppVersionHeader = "X-Client-App-Version";

    /// <summary>The key the parsed context is stored under, for the accessor to read.</summary>
    public const string ContextItemKey = "MusicRoom.ClientContext";

    /// <summary>The width of each value, matching the <c>audit_records</c> columns.</summary>
    public const int MaximumValueLength = 64;

    private static readonly string[] _mutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly RequestDelegate _next;

    /// <summary>Takes the next stage of the pipeline.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    public ClientContextMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Validates the headers, or refuses the request.</summary>
    /// <param name="httpContext">The request being handled.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!RequiresClientContext(httpContext))
        {
            await _next(httpContext);
            return;
        }

        string? platform = Read(httpContext, PlatformHeader);
        string? device = Read(httpContext, DeviceHeader);
        string? appVersion = Read(httpContext, AppVersionHeader);

        if (platform is null || device is null || appVersion is null)
        {
            // Bodiless: the missing header names are in the contract, and a response listing which one was
            // absent is a description of the client's own request that the client already has.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        httpContext.Items[ContextItemKey] = new ClientContextValues(
            platform,
            device,
            appVersion,
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier,
            // Present only when the bearer middleware has already validated a token, which is why this
            // middleware runs after UseAuthentication — SPEC-010.
            ClaimedUserId(httpContext));

        await _next(httpContext);
    }

    /// <summary>
    /// The account on the validated principal, or null when the request is anonymous. Nothing is trusted
    /// here that the bearer handler did not already verify.
    /// </summary>
    private static Guid? ClaimedUserId(HttpContext httpContext) =>
        Guid.TryParse(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value,
            out Guid userId)
            ? userId
            : null;

    private static bool RequiresClientContext(HttpContext httpContext) =>
        _mutatingMethods.Contains(httpContext.Request.Method, StringComparer.OrdinalIgnoreCase)
        && !httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the header value, or null when it is absent, blank or longer than the column that stores it.
    /// An overlong value is refused rather than truncated: a truncated audit record is a wrong one.
    /// </summary>
    private static string? Read(HttpContext httpContext, string header)
    {
        string? value = httpContext.Request.Headers[header].ToString();

        return string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueLength
            ? null
            : value;
    }
}

/// <summary>
/// The parsed header values, handed from the middleware to the accessor through <c>HttpContext.Items</c>.
/// </summary>
/// <param name="Platform">From <c>X-Client-Platform</c>.</param>
/// <param name="Device">From <c>X-Client-Device</c>.</param>
/// <param name="AppVersion">From <c>X-Client-App-Version</c>.</param>
/// <param name="CorrelationId">The request's trace id — ADR-0022.</param>
/// <param name="UserId">The validated account, or null when the request is anonymous.</param>
/// <remarks>
/// A `WebAPI` type rather than the `Application` record, so nothing in the pipeline depends on how the
/// values travelled. The accessor maps one to the other.
/// </remarks>
internal sealed record ClientContextValues(
    string Platform,
    string Device,
    string AppVersion,
    string CorrelationId,
    Guid? UserId);
