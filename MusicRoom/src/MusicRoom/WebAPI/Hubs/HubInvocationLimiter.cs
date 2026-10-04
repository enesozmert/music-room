using System.Collections.Concurrent;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// How many hub methods one connection may invoke in a minute — ADR-0030's call limit.
/// </summary>
/// <remarks>
/// A fixed window per connection id, counted in process. The connection is the right key: ADR-0030's case is one
/// authorised client spending an event's write path at request rate, and that client holds one connection. A
/// per-account key would also be defensible and is already what the REST vote limiter uses (ADR-0041), which is
/// why this one is deliberately the other half — the connection ceiling bounds how many of these a caller may
/// hold.
/// <para>
/// A singleton for the same reason the registry is: a scoped counter forgets every invocation at the end of the
/// one that recorded it.
/// </para>
/// </remarks>
internal sealed class HubInvocationLimiter
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    /// <summary>Takes the clock.</summary>
    /// <param name="time">The clock, injected so a test can move a window without waiting a minute.</param>
    internal HubInvocationLimiter(TimeProvider time) => _time = time;

    /// <summary>
    /// Records an invocation, and says whether it is allowed.
    /// </summary>
    /// <param name="connectionId">The connection invoking.</param>
    /// <param name="limits">The configured ceiling and window.</param>
    /// <returns>How long to wait when it is refused, or null when it is allowed.</returns>
    /// <remarks>
    /// Returns the wait rather than a bare bool, because the refusal has to say when to come back: SPEC-006 put
    /// `Retry-After` on every REST refusal for the same reason, and a limiter without it turns a client's backoff
    /// into a guess.
    /// </remarks>
    internal TimeSpan? Refuse(string connectionId, HubConnectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        DateTimeOffset nowUtc = _time.GetUtcNow();

        Window window = _windows.AddOrUpdate(
            connectionId,
            _ => new Window(nowUtc, 1),
            (_, existing) => existing.StartedAtUtc + HubConnectionLimits.InvocationWindow <= nowUtc
                ? new Window(nowUtc, 1)
                : existing with { Count = existing.Count + 1 });

        return window.Count > limits.InvocationsPerConnectionPerMinute
            ? window.StartedAtUtc + HubConnectionLimits.InvocationWindow - nowUtc
            : null;
    }

    /// <summary>Forgets a connection's window.</summary>
    /// <param name="connectionId">The connection that disconnected.</param>
    /// <remarks>
    /// Called on disconnect, or the dictionary grows by one entry per connection for the process's lifetime —
    /// which is the quiet kind of leak that only shows up after a long demo.
    /// </remarks>
    internal void Forget(string connectionId) => _windows.TryRemove(connectionId, out _);

    private sealed record Window(DateTimeOffset StartedAtUtc, int Count);
}
