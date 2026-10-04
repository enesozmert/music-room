using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Builds a real SignalR client against the test host — SPEC-016.
/// </summary>
/// <remarks>
/// A real client rather than a mocked <c>IHubContext</c>: what this spec has to prove is that a handshake is
/// refused without client context, that a join is authorised, and that a message actually arrives at somebody
/// who joined. A mock of the hub context proves none of those.
/// <para>
/// Long polling, because <c>TestServer</c> has no real socket. The transport is not what is under test, and
/// every path these tests exercise — the token in the query string, the client context in the query string,
/// the filter, the groups — is the same on either one.
/// </para>
/// </remarks>
internal static class HubClients
{
    /// <summary>The platform a test client reports.</summary>
    internal const string Platform = "ios";

    /// <summary>The device a test client reports.</summary>
    internal const string Device = "iPhone15";

    /// <summary>The application version a test client reports.</summary>
    internal const string AppVersion = "1.0.0";

    /// <summary>
    /// A connection carrying a token and, unless told otherwise, the three client values.
    /// </summary>
    /// <param name="factory">The test host.</param>
    /// <param name="accessToken">The signed JWT, or null to connect anonymously.</param>
    /// <param name="withClientContext">False to omit platform, device and appVersion — ADR-0019's refusal.</param>
    /// <param name="path">Which hub to connect to. Defaults to the playlist hub.</param>
    /// <param name="origin">An `Origin` header to send, for the handshake check ADR-0030 added. Null sends
    /// none, which is the native client's case.</param>
    /// <returns>A connection that has not been started.</returns>
    internal static HubConnection Build(
        WebApplicationFactory<Program> factory,
        string? accessToken,
        bool withClientContext = true,
        string? path = null,
        string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        string hubPath = path ?? PlaylistHub.Path;

        List<string> query = [];

        if (accessToken is not null)
        {
            // The documented SignalR mechanism, and the one SPEC-016 decision 4 narrowed to this path.
            query.Add($"access_token={Uri.EscapeDataString(accessToken)}");
        }

        if (withClientContext)
        {
            query.Add($"{HubClientContext.PlatformQuery}={Platform}");
            query.Add($"{HubClientContext.DeviceQuery}={Device}");
            query.Add($"{HubClientContext.AppVersionQuery}={AppVersion}");
        }

        Uri url = new(
            factory.Server.BaseAddress,
            query.Count == 0 ? hubPath : $"{hubPath}?{string.Join('&', query)}");

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;

                if (origin is not null)
                {
                    // A browser sets this itself; a test has to, because the handshake check is the one control
                    // that behaves differently for a browser and for the native client (ADR-0030).
                    options.Headers["Origin"] = origin;
                }
            })
            .Build();
    }
}
