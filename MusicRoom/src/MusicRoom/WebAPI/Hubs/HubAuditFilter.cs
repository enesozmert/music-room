using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Entities;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// Writes the audit record for a hub action, and puts the caller where the pipeline can read it — ADR-0019.
/// </summary>
/// <remarks>
/// **Two jobs, one filter** — ADR-0030 asked for the invocation limit here rather than in a second filter, so
/// there is one place that knows a hub invocation happened and one ordering to reason about. The limit runs
/// first: a refused invocation is recorded as `refused`, exactly as a refused join is, and never reaches the
/// hub method.
/// <para>
/// It also keeps the connection ceiling honest. `OnConnectedAsync` takes a slot in
/// <see cref="HubConnectionRegistry"/> and refuses the connection when there is none;
/// `OnDisconnectedAsync` releases it. The middleware refuses a handshake over the ceiling too, which is the
/// refusal a client sees reliably — but the count lives here, because two simultaneous negotiates can both pass
/// a check that only reads it (SPEC-026 decision 2).
/// </para>
/// <para>
/// The REST path gets its record from a MediatR behaviour, which hub invocations do not pass through. This
/// filter writes the **same** record for `JoinPlaylist` and `LeavePlaylist`, so the audit table has one
/// schema and one query surface, and subject V.6's "every action" is true on both transports.
/// </para>
/// <para>
/// Connect and disconnect are deliberately not audited: mobile networks reconnect constantly, and the noise
/// would bury the deliberate actions. They stay in the application log, where operational events belong.
/// </para>
/// <para>
/// A singleton, resolving per-invocation services from <see cref="HubInvocationContext.ServiceProvider"/>:
/// that is the invocation's own scope, which is where the scoped caller holder and the audit sink live.
/// </para>
/// <para>
/// **One filter for every hub.** ADR-0019 says a new hub method is audited because the filter is on the hub,
/// not because somebody remembered; two filters would make that false the first time one of them was forgotten
/// (SPEC-019 decision 3).
/// </para>
/// </remarks>
internal sealed class HubAuditFilter : IHubFilter
{
    private readonly HubConnectionRegistry _connections;
    private readonly HubInvocationLimiter _invocations;
    private readonly HubConnectionLimits _limits;
    private readonly ILogger<HubAuditFilter> _logger;

    /// <summary>Takes the ceiling, the call limit and a logger.</summary>
    /// <param name="connections">The authoritative connection counts.</param>
    /// <param name="invocations">The per-connection invocation windows.</param>
    /// <param name="limits">ADR-0030's numbers.</param>
    /// <param name="logger">Where a limiter trip is reported.</param>
    public HubAuditFilter(
        HubConnectionRegistry connections,
        HubInvocationLimiter invocations,
        HubConnectionLimits limits,
        ILogger<HubAuditFilter> logger)
    {
        _connections = connections;
        _invocations = invocations;
        _limits = limits;
        _logger = logger;
    }

    /// <summary>
    /// Takes a connection slot, or refuses the connection.
    /// </summary>
    /// <param name="context">The connection SignalR has accepted.</param>
    /// <param name="next">The rest of the connect pipeline.</param>
    /// <returns>A task that completes when the connection is established, or throws.</returns>
    /// <remarks>
    /// The authoritative half of the ceiling. The middleware has already refused a handshake that was over it,
    /// so reaching here and finding no room means two negotiates raced — rare, and the reason this check exists
    /// at all.
    /// </remarks>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string userKey = HubConnectionRegistry.UserKey(
            Claim(context.Context.User, ClaimTypes.NameIdentifier, "sub"));
        string addressKey = HubConnectionRegistry.AddressKey(Address(context.Context));

        if (!_connections.TryAdd(userKey, addressKey, _limits))
        {
            _logger.LogWarning(
                "A hub connection was refused on {HubName}: the concurrent connection ceiling is reached.",
                context.Hub.GetType().Name);

            throw new HubException("Too many concurrent connections.");
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch
        {
            // The slot is released here because `OnDisconnectedAsync` does not run for a connection that never
            // established: a failed connect would otherwise hold a slot until the process restarted.
            _connections.Release(userKey, addressKey);

            throw;
        }
    }

    /// <summary>Releases the connection's slot and forgets its invocation window.</summary>
    /// <param name="context">The connection going away.</param>
    /// <param name="exception">Why it went away, or null.</param>
    /// <param name="next">The rest of the disconnect pipeline.</param>
    /// <returns>A task that completes when the disconnect is handled.</returns>
    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            await next(context, exception).ConfigureAwait(false);
        }
        finally
        {
            // In a `finally`, because a hub's own `OnDisconnectedAsync` throwing must not leak a slot — a client
            // that reconnects would then be locked out by a bug in code that runs after it left.
            _connections.Release(
                HubConnectionRegistry.UserKey(Claim(context.Context.User, ClaimTypes.NameIdentifier, "sub")),
                HubConnectionRegistry.AddressKey(Address(context.Context)));

            _invocations.Forget(context.Context.ConnectionId);
        }
    }

    /// <summary>Dispatches the method, having set the caller, and records what became of it.</summary>
    /// <param name="invocationContext">The invocation SignalR is about to perform.</param>
    /// <param name="next">The rest of the invocation pipeline.</param>
    /// <returns>Whatever the hub method answered.</returns>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        CancellationToken cancellationToken = invocationContext.Context.ConnectionAborted;

        HubCaller caller = Caller(invocationContext);
        AuditedHubAction? action = AuditAction(invocationContext.HubMethodName);

        try
        {
            Throttle(invocationContext);

            object? result = await next(invocationContext).ConfigureAwait(false);

            await RecordAsync(invocationContext, caller, action, AuditOutcome.Completed, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        catch (HubException)
        {
            // A refused join is an action the user took and the server declined, which is exactly what the
            // REST path records as `refused`. Then it propagates: the client must still be told.
            await RecordAsync(invocationContext, caller, action, AuditOutcome.Refused, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Throws when this connection has spent its allowance for the window — ADR-0030's call limit.
    /// </summary>
    /// <remarks>
    /// Inside the try, so the refusal is audited `refused` by the same path that records a refused join: a client
    /// that is throttled took an action the server declined, which is what that outcome means.
    /// <para>
    /// The log line names the hub, the method and the wait, and nothing that identifies the account — SPEC-006's
    /// rule for a REST limiter trip, on a different transport. The account is in the audit record, which is the
    /// place that may name it.
    /// </para>
    /// </remarks>
    private void Throttle(HubInvocationContext invocationContext)
    {
        TimeSpan? wait = _invocations.Refuse(invocationContext.Context.ConnectionId, _limits);

        if (wait is null)
        {
            return;
        }

        _logger.LogWarning(
            "Hub invocation limit tripped on {HubName}.{HubMethodName}. Retry after {RetryAfterSeconds}s.",
            invocationContext.Hub.GetType().Name,
            invocationContext.HubMethodName,
            (int)Math.Ceiling(wait.Value.TotalSeconds));

        throw new HubException("Too many hub invocations. Slow down and try again.");
    }

    /// <summary>The caller's address, as the connection reports it.</summary>
    private static string? Address(HubCallerContext context) =>
        context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

    /// <summary>Fills the per-invocation holder from the connection and the validated principal.</summary>
    private static HubCaller Caller(HubInvocationContext invocationContext)
    {
        HubCaller caller = invocationContext.ServiceProvider.GetRequiredService<HubCaller>();

        caller.UserId = Claim(invocationContext.Context.User, ClaimTypes.NameIdentifier, "sub");
        caller.SessionId = Claim(invocationContext.Context.User, ClaimTypes.Sid, "sid");

        if (invocationContext.Context.Items.TryGetValue(HubClientContext.ItemKey, out object? stored)
            && stored is HubClientContext context)
        {
            caller.Context = new ClientContext(
                context.Platform,
                context.Device,
                context.AppVersion,
                context.CorrelationId,
                caller.UserId);
        }

        return caller;
    }

    /// <summary>The audit action for a hub method, or null for a method that is not audited.</summary>
    /// <remarks>
    /// The suffix is trimmed first, because SignalR reports the C# method name while a client calls the wire
    /// name the <c>HubMethodName</c> attribute publishes. Matching on one spelling only would silently audit
    /// nothing, which is the failure mode ADR-0019 exists to prevent.
    /// </remarks>
    private static AuditedHubAction? AuditAction(string hubMethodName)
    {
        const string asyncSuffix = "Async";

        string name = hubMethodName.EndsWith(asyncSuffix, StringComparison.Ordinal)
            ? hubMethodName[..^asyncSuffix.Length]
            : hubMethodName;

        return name switch
        {
            PlaylistHub.JoinMethod => new AuditedHubAction("playlist.join", "playlist"),
            PlaylistHub.LeaveMethod => new AuditedHubAction("playlist.leave", "playlist"),
            EventHub.JoinMethod => new AuditedHubAction("event.join", "event"),
            EventHub.LeaveMethod => new AuditedHubAction("event.leave", "event"),
            _ => null,
        };
    }

    private static Guid? Claim(ClaimsPrincipal? principal, string mappedType, string rawType)
    {
        string? value = principal?.FindFirst(mappedType)?.Value ?? principal?.FindFirst(rawType)?.Value;

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }

    /// <summary>Writes the record, when the method is one ADR-0019 names.</summary>
    /// <remarks>
    /// The sink opens no transaction of its own, and this filter opens none either: one insert is atomic by
    /// itself, and there is no second write for it to be atomic *with* — a join changes no other row.
    /// </remarks>
    private static async Task RecordAsync(
        HubInvocationContext invocationContext,
        HubCaller caller,
        AuditedHubAction? action,
        AuditOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (action is null || caller.Context is not ClientContext context)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;

        IAuditSink sink = invocationContext.ServiceProvider.GetRequiredService<IAuditSink>();

        await sink.WriteAsync(
            new AuditRecord
            {
                Id = Guid.NewGuid(),
                UserId = caller.UserId,
                Action = action.Action,
                TargetType = action.TargetType,
                TargetId = invocationContext.HubMethodArguments is [Guid playlistId, ..] ? playlistId : null,
                Platform = context.Platform,
                Device = context.Device,
                AppVersion = context.AppVersion,
                OccurredAtUtc = nowUtc,
                CorrelationId = context.CorrelationId,
                Outcome = outcome.ToString().ToLowerInvariant(),
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>What one audited hub method is called in the audit log.</summary>
/// <param name="Action">The stable action string, such as <c>event.join</c>.</param>
/// <param name="TargetType">What kind of thing it acted on, such as <c>event</c>.</param>
/// <remarks>
/// A record rather than two parallel switch expressions, so a hub method cannot end up with one hub's action
/// and another's target type.
/// </remarks>
internal sealed record AuditedHubAction(string Action, string TargetType);
