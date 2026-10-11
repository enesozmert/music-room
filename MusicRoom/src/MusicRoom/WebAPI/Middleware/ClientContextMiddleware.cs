using System.Diagnostics;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Middleware;

/// <summary>
/// Enforces the mandatory client headers on every mutating request — SDD §10.7 steps 1 to 3.
/// </summary>
/// <remarks>
/// Subject V.6 requires platform, device and app version on every client action, so a mutation without
/// them is refused rather than defaulted: a silent default would void the requirement while appearing to
/// satisfy it. A read is untouched, and so are the health probes — a probe is not a client action.
/// <para>
/// A SignalR handshake is refused by the same rule and with the same `400`, reading the three values from the
/// query string because a WebSocket handshake cannot set headers — ADR-0019, SPEC-016. The requests that
/// follow it are not re-checked: the connection already proved it had them.
/// </para>
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

    /// <summary>
    /// Where the SignalR hubs live. One segment, so every hub under it is covered.
    /// </summary>
    public const string HubPaths = "/hubs";

    /// <summary>The last segment of a SignalR handshake request.</summary>
    public const string NegotiateSegment = "/negotiate";

    private static readonly string[] _mutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly RequestDelegate _next;
    private readonly HubConnectionRegistry _connections;
    private readonly HubConnectionLimits _limits;

    /// <summary>Takes the next stage of the pipeline and the realtime ceilings.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="connections">The current connection counts, for the handshake ceiling.</param>
    /// <param name="limits">ADR-0030's numbers and the allowed origins.</param>
    public ClientContextMiddleware(
        RequestDelegate next,
        HubConnectionRegistry connections,
        HubConnectionLimits limits)
    {
        _next = next;
        _connections = connections;
        _limits = limits;
    }

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

        // On the hub path the values arrive in the query string, because a WebSocket handshake cannot set
        // headers. Everywhere else they are headers and only headers — SPEC-016 decision 3.
        bool fromQuery = IsHubHandshake(httpContext);

        if (fromQuery && !await HandshakeIsAllowedAsync(httpContext).ConfigureAwait(false))
        {
            return;
        }

        if (!TryReadContext(httpContext, fromQuery))
        {
            // Bodiless: the missing header names are in the contract, and a response listing which one was
            // absent is a description of the client's own request that the client already has.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await _next(httpContext);
    }

    /// <summary>
    /// Reads the three values and stores them, or reports that one is missing.
    /// </summary>
    /// <param name="httpContext">The request being handled.</param>
    /// <param name="fromQuery">Whether the values come from the handshake's query string.</param>
    /// <returns>False when a value is absent, blank or overlong.</returns>
    /// <remarks>
    /// Its own method because SPEC-026 added the handshake checks above and pushed <c>InvokeAsync</c> past the
    /// complexity gate; the branches here are the same ones it had.
    /// </remarks>
    private static bool TryReadContext(HttpContext httpContext, bool fromQuery)
    {
        string? platform = Read(httpContext, PlatformHeader, fromQuery ? "platform" : null);
        string? device = Read(httpContext, DeviceHeader, fromQuery ? "device" : null);
        string? appVersion = Read(httpContext, AppVersionHeader, fromQuery ? "appVersion" : null);

        if (platform is null || device is null || appVersion is null)
        {
            return false;
        }

        httpContext.Items[ContextItemKey] = new ClientContextValues(
            platform,
            device,
            appVersion,
            Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier,
            // Present only when the bearer middleware has already validated a token, which is why this
            // middleware runs after UseAuthentication — SPEC-010.
            ClaimedUserId(httpContext),
            PreferredLanguage(httpContext));

        return true;
    }

    /// <summary>
    /// Refuses a handshake from a disallowed origin, or one that is already at the connection ceiling.
    /// </summary>
    /// <param name="httpContext">The negotiate being handled.</param>
    /// <returns>False when the handshake has been refused and the response is written.</returns>
    /// <remarks>
    /// Here rather than in the hub, for the reason SPEC-016 recorded: a refusal thrown in `OnConnectedAsync` does
    /// not reliably fail a client's `StartAsync` over long polling, so a check the client must see belongs in the
    /// middleware. The authoritative ceiling is still the filter's — this one reads a count it does not own, and
    /// two simultaneous negotiates can both pass it (SPEC-026 decision 2).
    /// <para>
    /// `403` for the origin and `429` for the ceiling: one is "not you, ever", the other is "not now". A client
    /// can retry the second and should not retry the first.
    /// </para>
    /// </remarks>
    private async Task<bool> HandshakeIsAllowedAsync(HttpContext httpContext)
    {
        string? origin = httpContext.Request.Headers.Origin.FirstOrDefault();

        if (!_limits.AllowsOrigin(origin))
        {
            // Bodiless, and it names neither the allowed list nor the reason: a response that described the
            // policy would be a probe for finding an origin that passes it.
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;

            return false;
        }

        bool hasRoom = _connections.HasRoomFor(
            HubConnectionRegistry.UserKey(ClaimedUserId(httpContext)),
            _limits.ConnectionsPerUser)
            && _connections.HasRoomFor(
                HubConnectionRegistry.AddressKey(httpContext.Connection.RemoteIpAddress?.ToString()),
                _limits.ConnectionsPerAddress);

        if (hasRoom)
        {
            return true;
        }

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // The wait is the honest one: a slot frees when a connection closes, and nothing here knows when that
        // will be. A fixed number would be a guess dressed as a promise.
        httpContext.Response.Headers.RetryAfter = "5";

        await httpContext.Response.CompleteAsync().ConfigureAwait(false);

        return false;
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

    /// <summary>
    /// Whether this request must carry the three headers.
    /// </summary>
    /// <remarks>
    /// A read is exempt, and so are the health probes — a probe is not a client action. The hub is exempt
    /// too, and not because it is excused: a SignalR negotiate is a `POST` that carries no client headers by
    /// design, because a WebSocket handshake cannot set them. The hub reads the same three values off the
    /// handshake's query string and refuses a connection without them, so the requirement is enforced there
    /// instead of here — ADR-0019, SPEC-016 decision 3.
    /// </remarks>
    private static bool RequiresClientContext(HttpContext httpContext) =>
        IsHubHandshake(httpContext)
        || (_mutatingMethods.Contains(httpContext.Request.Method, StringComparer.OrdinalIgnoreCase)
            && !httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            && !httpContext.Request.Path.StartsWithSegments(HubPaths, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which language the caller reads, from <c>Accept-Language</c> — SPEC-052 decision 6.
    /// </summary>
    /// <remarks>
    /// A prefix match on the first tag, and nothing more: `tr`, `tr-TR` and `tr-CY` are Turkish, everything
    /// else is English. Quality values are not weighed, because weighing them would be choosing between two
    /// languages a reader listed — and with exactly two supported, the first tag already said which one
    /// they prefer.
    /// <para>
    /// A header is not a credential and is not trusted for anything: the worst a forged one does is send a
    /// message in the wrong language.
    /// </para>
    /// </remarks>
    private static MailLanguage PreferredLanguage(HttpContext httpContext)
    {
        const string turkish = "tr";

        string? requested = httpContext.Request.Headers.AcceptLanguage.ToString();

        if (string.IsNullOrWhiteSpace(requested))
        {
            return MailLanguage.English;
        }

        string first = requested.Split(',')[0].Split(';')[0].Trim();

        return first.StartsWith(turkish, StringComparison.OrdinalIgnoreCase)
            ? MailLanguage.Turkish
            : MailLanguage.English;
    }

    /// <summary>
    /// Whether this is the `POST .../negotiate` that opens a hub connection.
    /// </summary>
    /// <remarks>
    /// The handshake, and only the handshake: the polling and sending requests that follow carry the same
    /// query string, and checking each of them would buy nothing but work per message.
    /// </remarks>
    private static bool IsHubHandshake(HttpContext httpContext) =>
        httpContext.Request.Path.StartsWithSegments(HubPaths, StringComparison.OrdinalIgnoreCase)
        && httpContext.Request.Path.Value?.EndsWith(NegotiateSegment, StringComparison.OrdinalIgnoreCase)
            == true;

    /// <summary>
    /// Returns the value, or null when it is absent, blank or longer than the column that stores it.
    /// An overlong value is refused rather than truncated: a truncated audit record is a wrong one.
    /// </summary>
    /// <param name="httpContext">The request being read.</param>
    /// <param name="header">The header to read.</param>
    /// <param name="queryKey">A query parameter to fall back to, for a hub handshake, or null.</param>
    private static string? Read(HttpContext httpContext, string header, string? queryKey)
    {
        string? value = httpContext.Request.Headers[header].ToString();

        if (string.IsNullOrWhiteSpace(value) && queryKey is not null)
        {
            value = httpContext.Request.Query[queryKey].ToString();
        }

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
/// <param name="Language">Which language the caller reads, from <c>Accept-Language</c> — SPEC-052.</param>
/// <remarks>
/// A `WebAPI` type rather than the `Application` record, so nothing in the pipeline depends on how the
/// values travelled. The accessor maps one to the other.
/// </remarks>
internal sealed record ClientContextValues(
    string Platform,
    string Device,
    string AppVersion,
    string CorrelationId,
    Guid? UserId,
    MailLanguage Language);
