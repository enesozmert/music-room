using System.Collections.Concurrent;

namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// How many connections each account and each address currently holds — ADR-0030's ceiling.
/// </summary>
/// <remarks>
/// The authoritative count, and the only place that knows it. A singleton, because the whole point is state that
/// outlives a request: a scoped registry would forget every connection at the end of the request that opened it.
/// <para>
/// **Per process**, exactly like every other limiter here — issue O8. One instance is the ceiling
/// ADR-0015 deploys, and a second instance would hold its own counts.
/// </para>
/// <para>
/// The release path is what the tests are built around. A ceiling that counts up and never down turns a client
/// that reconnects five times into a client that can never connect again, which is worse than the abuse it was
/// meant to bound.
/// </para>
/// <para>
/// Public, not because anything outside this assembly should use it, but because
/// <c>ClientContextMiddleware</c> is public and <c>UseMiddleware</c> activates it through a public constructor —
/// a public type cannot take a less accessible parameter.
/// </para>
/// </remarks>
public sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>How many connections this key holds right now.</summary>
    /// <param name="key">A user key or an address key.</param>
    /// <returns>The count, or zero.</returns>
    public int CountFor(string key) => _counts.TryGetValue(key, out int count) ? count : 0;

    /// <summary>
    /// Whether one more connection for this key would stay inside the ceiling.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <param name="ceiling">The ceiling for it.</param>
    /// <returns>True when a connection may be opened.</returns>
    /// <remarks>
    /// A read without a reservation, for the handshake check: two simultaneous negotiates can both pass it, which
    /// is why <see cref="TryAdd"/> and not this method is what makes the ceiling true (SPEC-026 decision 2).
    /// </remarks>
    public bool HasRoomFor(string key, int ceiling) => CountFor(key) < ceiling;

    /// <summary>
    /// Takes a slot for each key, or takes nothing.
    /// </summary>
    /// <param name="userKey">The account's key.</param>
    /// <param name="addressKey">The address's key.</param>
    /// <param name="limits">The ceilings to hold them to.</param>
    /// <returns>True when both slots were taken.</returns>
    /// <remarks>
    /// Both or neither, under one lock: taking the user's slot and then failing on the address would leak a slot
    /// that no disconnect will ever release, because no connection was opened to release it.
    /// </remarks>
    public bool TryAdd(string userKey, string addressKey, HubConnectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        lock (_gate)
        {
            if (!HasRoomFor(userKey, limits.ConnectionsPerUser)
                || !HasRoomFor(addressKey, limits.ConnectionsPerAddress))
            {
                return false;
            }

            _counts.AddOrUpdate(userKey, 1, (_, count) => count + 1);
            _counts.AddOrUpdate(addressKey, 1, (_, count) => count + 1);

            return true;
        }
    }

    /// <summary>Releases a slot for each key.</summary>
    /// <param name="userKey">The account's key.</param>
    /// <param name="addressKey">The address's key.</param>
    public void Release(string userKey, string addressKey)
    {
        lock (_gate)
        {
            Decrement(userKey);
            Decrement(addressKey);
        }
    }

    /// <summary>The key a user's connections are counted under.</summary>
    /// <param name="userId">The validated account, or null.</param>
    /// <returns>The key.</returns>
    public static string UserKey(Guid? userId) =>
        "user:" + (userId?.ToString() ?? "anonymous");

    /// <summary>The key an address's connections are counted under.</summary>
    /// <param name="address">The remote address as a string, or null.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// An absent address shares one bucket rather than getting its own, as the REST limiter does: an unkeyable
    /// caller must not be the cheapest caller.
    /// </remarks>
    public static string AddressKey(string? address) =>
        "ip:" + (string.IsNullOrWhiteSpace(address) ? "unknown" : address);

    private void Decrement(string key)
    {
        if (!_counts.TryGetValue(key, out int count))
        {
            return;
        }

        if (count <= 1)
        {
            _counts.TryRemove(key, out _);

            return;
        }

        _counts[key] = count - 1;
    }
}
