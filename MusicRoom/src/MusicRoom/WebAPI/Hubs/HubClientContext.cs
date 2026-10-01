using System.Diagnostics;
using MusicRoom.WebAPI.Middleware;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// The three client values subject V.6 requires, captured at the handshake — ADR-0019.
/// </summary>
/// <param name="Platform">The client platform.</param>
/// <param name="Device">The client device.</param>
/// <param name="AppVersion">The client application version.</param>
/// <param name="CorrelationId">The handshake's trace id, so a hub record joins the application log.</param>
/// <remarks>
/// Public because the three query names are part of the published contract — ENDPOINTS §9 — exactly as
/// <see cref="ClientContextMiddleware"/>'s header names are.
/// <para>
/// A hub method invocation carries no HTTP headers, so these are read once — from the handshake — and held
/// on the connection. The query string is the primary source because a WebSocket handshake from a browser
/// cannot set headers; a header is accepted too, so a client that can send one is not forced into the URL.
/// </para>
/// </remarks>
public sealed record HubClientContext(
    string Platform,
    string Device,
    string AppVersion,
    string CorrelationId)
{
    /// <summary>The key this is held under on the connection.</summary>
    public const string ItemKey = "MusicRoom.HubClientContext";

    /// <summary>The query parameter carrying the client platform.</summary>
    public const string PlatformQuery = "platform";

    /// <summary>The query parameter carrying the client device.</summary>
    public const string DeviceQuery = "device";

    /// <summary>The query parameter carrying the client application version.</summary>
    public const string AppVersionQuery = "appVersion";

    /// <summary>Reads the context off the handshake, or null when any of the three is missing.</summary>
    /// <param name="httpContext">The handshake request, which SignalR keeps on the connection.</param>
    /// <returns>The context, or null — which the hub turns into a refused connection.</returns>
    /// <remarks>
    /// Null rather than a default: ADR-0019 refuses a handshake without these values, mirroring the `400` a
    /// REST mutation gets. Defaulting them would leave the audit log full of rows nobody can attribute, which
    /// is the failure subject V.6 is written against.
    /// </remarks>
    public static HubClientContext? FromHandshake(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        string? platform = Read(httpContext, PlatformQuery, ClientContextMiddleware.PlatformHeader);
        string? device = Read(httpContext, DeviceQuery, ClientContextMiddleware.DeviceHeader);
        string? appVersion = Read(httpContext, AppVersionQuery, ClientContextMiddleware.AppVersionHeader);

        return platform is null || device is null || appVersion is null
            ? null
            : new HubClientContext(
                platform,
                device,
                appVersion,
                Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
    }

    /// <summary>
    /// One value, from the query string or the matching header, bounded to the audit column's width.
    /// </summary>
    private static string? Read(HttpContext httpContext, string queryKey, string headerName)
    {
        string? value = httpContext.Request.Query[queryKey].ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            value = httpContext.Request.Headers[headerName].ToString();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        // Bounded like the REST path's, for the same reason: the column is 64 characters and a client does
        // not get to decide how much of the audit table one handshake occupies.
        return trimmed.Length > ClientContextMiddleware.MaximumValueLength ? null : trimmed;
    }
}
