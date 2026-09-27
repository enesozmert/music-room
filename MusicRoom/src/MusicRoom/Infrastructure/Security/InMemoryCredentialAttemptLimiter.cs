using System.Collections.Concurrent;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The per-account credential limit, held in this process — SPEC-006 escalation 3.
/// </summary>
/// <remarks>
/// A fixed number of chargeable attempts per window, then a penalty that doubles with each further
/// consecutive violation up to a ceiling. That growth is the "progressive backoff on repeated login
/// failure" `authentication-authorization` requires and no built-in limiter primitive expresses.
/// <para>
/// State is a <see cref="ConcurrentDictionary{TKey,TValue}"/>, pruned as it is used, rather than
/// <c>IMemoryCache</c>: that package is not in this project's dependency graph and adding one is a
/// decision, not a convenience (ADR-0021). The consequences are the ones the owner accepted — a
/// restart forgives every caller, and the count is per instance, so several instances would each hold
/// their own (issue O8).
/// </para>
/// <para>
/// Keys are addresses. Nothing here logs one: a log line naming the address that ran out of attempts
/// is an enumeration oracle written to disk.
/// </para>
/// </remarks>
public sealed class InMemoryCredentialAttemptLimiter : ICredentialAttemptLimiter
{
    private readonly ConcurrentDictionary<string, AttemptHistory> _histories =
        new(StringComparer.Ordinal);

    private readonly CredentialAttemptLimits _limits;
    private readonly TimeProvider _time;

    /// <summary>Takes the approved limits and a clock.</summary>
    /// <param name="limits">Permits, window and penalty ceiling — SPEC-006 escalation 1.</param>
    /// <param name="time">The clock, so a test asserts the curve instead of sleeping through it.</param>
    public InMemoryCredentialAttemptLimiter(CredentialAttemptLimits limits, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(time);

        _limits = limits;
        _time = time;
    }

    /// <inheritdoc />
    public CredentialAttemptDecision Check(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        DateTimeOffset nowUtc = _time.GetUtcNow();

        if (!_histories.TryGetValue(key, out AttemptHistory? history))
        {
            return new CredentialAttemptDecision(Allowed: true, TimeSpan.Zero);
        }

        lock (history)
        {
            if (history.BlockedUntilUtc > nowUtc)
            {
                return new CredentialAttemptDecision(Allowed: false, history.BlockedUntilUtc - nowUtc);
            }

            // The window has moved on, so the attempts inside it no longer count. The violation count
            // is kept: it is what makes the next penalty longer than the last.
            if (history.WindowStartedAtUtc + _limits.Window <= nowUtc)
            {
                history.Attempts = 0;
                history.WindowStartedAtUtc = nowUtc;
            }

            return new CredentialAttemptDecision(Allowed: true, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public void RecordChargeableAttempt(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        DateTimeOffset nowUtc = _time.GetUtcNow();
        AttemptHistory history = _histories.GetOrAdd(key, _ => new AttemptHistory(nowUtc));

        lock (history)
        {
            if (history.WindowStartedAtUtc + _limits.Window <= nowUtc)
            {
                history.Attempts = 0;
                history.WindowStartedAtUtc = nowUtc;
            }

            history.Attempts++;

            if (history.Attempts >= _limits.PermitsPerWindow)
            {
                history.Violations++;
                history.BlockedUntilUtc = nowUtc + _limits.PenaltyFor(history.Violations);
                history.Attempts = 0;
                history.WindowStartedAtUtc = nowUtc;
            }
        }

        PruneExpired(nowUtc);
    }

    /// <inheritdoc />
    public void Reset(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _histories.TryRemove(key, out _);
    }

    /// <summary>
    /// Drops histories that have neither an open window nor a live block, so the dictionary cannot grow
    /// without bound under the very loop this limiter exists to stop.
    /// </summary>
    private void PruneExpired(DateTimeOffset nowUtc)
    {
        // The retention floor is the longest penalty a key could still be serving.
        DateTimeOffset cutoff = nowUtc - _limits.MaximumPenalty;

        foreach (KeyValuePair<string, AttemptHistory> entry in _histories)
        {
            AttemptHistory history = entry.Value;

            lock (history)
            {
                if (history.BlockedUntilUtc <= nowUtc && history.WindowStartedAtUtc <= cutoff)
                {
                    _histories.TryRemove(entry.Key, out _);
                }
            }
        }
    }

    /// <summary>One key's attempt history. Mutated only under its own lock.</summary>
    private sealed class AttemptHistory
    {
        internal AttemptHistory(DateTimeOffset windowStartedAtUtc) =>
            WindowStartedAtUtc = windowStartedAtUtc;

        /// <summary>When the current window began.</summary>
        internal DateTimeOffset WindowStartedAtUtc { get; set; }

        /// <summary>When a penalty ends. Default is the past, which means no penalty.</summary>
        internal DateTimeOffset BlockedUntilUtc { get; set; }

        /// <summary>Chargeable attempts inside the current window.</summary>
        internal int Attempts { get; set; }

        /// <summary>How many times this key has exhausted a window. Drives the penalty.</summary>
        internal int Violations { get; set; }
    }
}

/// <summary>
/// The per-account numbers the owner approved on 2026-09-27 — SPEC-006 escalation 1.
/// </summary>
/// <remarks>
/// Declared beside the limiter rather than in its own file: it exists only to configure it, and
/// SPEC-006's Files table is the contract for what this change may create.
/// </remarks>
public sealed class CredentialAttemptLimits
{
    /// <summary>The approved per-account allowance for signing in: 5 failures per 5 minutes.</summary>
    public const int DefaultPermitsPerWindow = 5;

    /// <summary>The approved window, in minutes.</summary>
    public const int DefaultWindowMinutes = 5;

    /// <summary>
    /// How many doublings the penalty may take. The approved table names no ceiling, so it derives from
    /// the window: one window on the first violation, eight on the fourth and after.
    /// </summary>
    public const int DefaultMaximumPenaltyWindows = 8;

    /// <summary>Validates the numbers and keeps them.</summary>
    /// <param name="permitsPerWindow">Chargeable attempts allowed inside one window.</param>
    /// <param name="window">The window those attempts are counted in.</param>
    /// <param name="maximumPenaltyWindows">The penalty ceiling, in whole windows.</param>
    /// <exception cref="ArgumentOutOfRangeException">Any number is not positive.</exception>
    public CredentialAttemptLimits(int permitsPerWindow, TimeSpan window, int maximumPenaltyWindows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(permitsPerWindow, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumPenaltyWindows, 0);

        PermitsPerWindow = permitsPerWindow;
        Window = window;
        MaximumPenaltyWindows = maximumPenaltyWindows;
    }

    /// <summary>Chargeable attempts allowed inside one window.</summary>
    public int PermitsPerWindow { get; }

    /// <summary>The window attempts are counted in.</summary>
    public TimeSpan Window { get; }

    /// <summary>The penalty ceiling, in whole windows.</summary>
    public int MaximumPenaltyWindows { get; }

    /// <summary>The longest penalty this configuration can produce.</summary>
    public TimeSpan MaximumPenalty => Window * MaximumPenaltyWindows;

    /// <summary>
    /// The wait after a given number of consecutive violations: one window, then doubling, capped.
    /// </summary>
    /// <param name="violations">How many windows this key has exhausted, first one included.</param>
    /// <returns>The wait to report in <c>Retry-After</c>.</returns>
    public TimeSpan PenaltyFor(int violations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(violations, 0);

        // 1, 2, 4, 8 windows. Computed on the exponent so a large violation count cannot overflow the
        // multiplication before the cap is applied.
        int windows = violations >= MaximumPenaltyWindows
            ? MaximumPenaltyWindows
            : Math.Min(MaximumPenaltyWindows, 1 << Math.Min(violations - 1, 30));

        return Window * windows;
    }
}
