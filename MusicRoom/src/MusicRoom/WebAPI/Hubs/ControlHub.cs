using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// Control delegation, pushed to the people it concerns — SDD §9.2, ENDPOINTS §9, ADR-0004.
/// </summary>
/// <remarks>
/// **It has no invocable method.** The group is <c>user:{userId}</c>, taken from the validated token and joined
/// automatically on connect, so a client cannot ask to listen to somebody else's control because it cannot ask at
/// all. That is the strongest form of `signalr-realtime`'s server-side authorisation rule, and the only one
/// available when the group is a person rather than a shared aggregate.
/// <para>
/// Nothing here is audited, which is ADR-0019's own answer: that record audits hub **methods** — client actions —
/// and excludes connect and disconnect as reconnect noise. A hub with no methods has no client action to record,
/// and the control changes themselves are already audited on the REST side as `device.grant-control` and
/// `device.release-control` (SPEC-020).
/// </para>
/// </remarks>
[Authorize]
public sealed class ControlHub : Hub
{
    /// <summary>
    /// Where the hub is mapped. Under <see cref="Middleware.ClientContextMiddleware.HubPaths"/>, which is what
    /// makes the handshake carry platform, device and appVersion or be refused.
    /// </summary>
    public const string Path = "/hubs/control";

    /// <summary>The `ControlGranted` event name.</summary>
    public const string ControlGrantedEvent = "ControlGranted";

    /// <summary>The `ControlRevoked` event name.</summary>
    public const string ControlRevokedEvent = "ControlRevoked";

    /// <summary>The `ControlTakenOver` event name.</summary>
    public const string ControlTakenOverEvent = "ControlTakenOver";

    /// <summary>The group one account's control notices go to.</summary>
    /// <param name="userId">The account.</param>
    /// <returns>The group name, <c>user:{userId}</c> — SDD §9.2.</returns>
    public static string Group(Guid userId) =>
        string.Create(CultureInfo.InvariantCulture, $"user:{userId}");

    /// <summary>Captures the client context and joins the caller's own group.</summary>
    /// <returns>A task that completes when the connection is listening.</returns>
    /// <exception cref="HubException">
    /// The handshake carried no platform, device and app version, or no account could be read from the token. The
    /// middleware has already answered `400` for the first, so reaching here without either means the pipeline
    /// was bypassed — refuse rather than open a connection that listens to nothing or, worse, to a guess.
    /// </exception>
    public override async Task OnConnectedAsync()
    {
        HubClientContext context = HubClientContext.FromHandshake(Context.GetHttpContext())
            ?? throw new HubException(
                "A hub handshake must carry platform, device and appVersion — ENDPOINTS §9.");

        Context.Items[HubClientContext.ItemKey] = context;

        Guid userId = CallerId()
            ?? throw new HubException("No account could be read from the presented token.");

        await Groups
            .AddToGroupAsync(Context.ConnectionId, Group(userId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The account on the validated principal.
    /// </summary>
    /// <remarks>
    /// Read from the token and never from an argument, which is the whole mechanism: there is no argument.
    /// </remarks>
    private Guid? CallerId()
    {
        string? value = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }
}
