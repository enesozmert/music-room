using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Waits until the server has finished <c>OnConnectedAsync</c> for a connection — L-042's fence, for a hub that
/// has no method to round-trip through.
/// </summary>
/// <remarks>
/// SignalR writes the handshake response **before** it dispatches <c>OnConnectedAsync</c>, so the client's
/// <c>StartAsync</c> returns while the server is still joining the groups. Every other broadcast suite closes
/// that window by invoking the hub's own join method, which is a round trip and therefore a fence. `ControlHub`
/// has no invocable method on purpose — the group is the person on the token — so there is nothing to invoke,
/// and a test that only awaits <c>StartAsync</c> races the broadcast it is waiting for.
/// <para>
/// This filter is the fence instead. It is registered on the test host only, it adds nothing to the production
/// pipeline, and it signals after <c>next</c> — which is after `HubAuditFilter` and after the hub's own
/// <c>OnConnectedAsync</c>, so the group membership the broadcast needs already exists when
/// <see cref="StartAsync" /> returns. Waiting a fixed delay instead would be proving a negative by sleeping,
/// which is the mistake L-042 was written about.
/// </para>
/// </remarks>
internal sealed class HubConnectedBarrier : IHubFilter
{
    /// <summary>How long a test waits for the server to finish connecting before calling it broken.</summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _connected =
        new(StringComparer.Ordinal);

    /// <summary>Installs the barrier on a test host.</summary>
    /// <param name="webHost">The builder the suite is configuring.</param>
    /// <returns>The barrier the suite starts its connections through.</returns>
    internal static HubConnectedBarrier Install(IWebHostBuilder webHost)
    {
        ArgumentNullException.ThrowIfNull(webHost);

        HubConnectedBarrier barrier = new();

        webHost.ConfigureTestServices(services =>
            services.Configure<HubOptions>(options => options.AddFilter(barrier)));

        return barrier;
    }

    /// <summary>Signals the connection once the server is really listening.</summary>
    /// <param name="context">The connection SignalR is opening.</param>
    /// <param name="next">The rest of the connect pipeline, including the hub's own override.</param>
    /// <returns>A task that completes when the connection is open.</returns>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        await next(context).ConfigureAwait(false);

        Signal(context.Context.ConnectionId).TrySetResult();
    }

    /// <summary>Starts a connection and waits for the server side of it.</summary>
    /// <param name="connection">A connection built by <see cref="HubClients" />, not yet started.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the server has joined the connection to its groups.</returns>
    internal async Task StartAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.StartAsync(cancellationToken).ConfigureAwait(false);

        string connectionId = connection.ConnectionId
            ?? throw new InvalidOperationException("A started connection has an id.");

        await Signal(connectionId).Task.WaitAsync(_patience, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The promise for one connection, whichever side arrives first.</summary>
    private TaskCompletionSource Signal(string connectionId) =>
        _connected.GetOrAdd(
            connectionId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
