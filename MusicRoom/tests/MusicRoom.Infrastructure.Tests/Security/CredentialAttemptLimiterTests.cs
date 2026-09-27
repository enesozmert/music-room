using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// The per-account half of the credential limit — SPEC-006 escalation 1 and 3.
/// </summary>
/// <remarks>
/// The clock is injected, so the backoff curve is asserted rather than slept through. A test that
/// waited out a five-minute window would not be run.
/// </remarks>
public sealed class CredentialAttemptLimiterTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string Key = "player@example.com";
    internal const int Permits = 3;

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(5);

    /// <summary>An unknown key is allowed, and nothing is stored until an attempt is charged.</summary>
    [Fact]
    public void Check_AnUnknownKey_IsAllowed()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, _) = Build();

        // Act
        CredentialAttemptDecision decision = limiter.Check(Key);

        // Assert
        Assert.True(decision.Allowed);
        Assert.Equal(TimeSpan.Zero, decision.RetryAfter);
    }

    /// <summary>Attempts below the limit are allowed; the one that exhausts it blocks the next.</summary>
    [Fact]
    public void Check_AfterTheLimitIsExhausted_IsRefusedForOneWindow()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, _) = Build();

        // Act
        for (int attempt = 0; attempt < Permits; attempt++)
        {
            Assert.True(limiter.Check(Key).Allowed);
            limiter.RecordChargeableAttempt(Key);
        }

        CredentialAttemptDecision decision = limiter.Check(Key);

        // Assert — the first penalty is one window, which is what Retry-After reports.
        Assert.False(decision.Allowed);
        Assert.Equal(_window, decision.RetryAfter);
    }

    /// <summary>
    /// The penalty doubles with each further violation — the progressive backoff
    /// `authentication-authorization` requires.
    /// </summary>
    [Fact]
    public void Check_OnRepeatedViolations_LengthensThePenalty()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, TestTimeProvider time) = Build();

        // Act — exhaust the limit three times over, waiting out each penalty in between.
        TimeSpan[] penalties = new TimeSpan[3];

        for (int violation = 0; violation < penalties.Length; violation++)
        {
            for (int attempt = 0; attempt < Permits; attempt++)
            {
                limiter.RecordChargeableAttempt(Key);
            }

            penalties[violation] = limiter.Check(Key).RetryAfter;
            time.Advance(penalties[violation] + TimeSpan.FromSeconds(1));
        }

        // Assert — one window, then two, then four.
        Assert.Equal(_window, penalties[0]);
        Assert.Equal(_window * 2, penalties[1]);
        Assert.Equal(_window * 4, penalties[2]);
    }

    /// <summary>The penalty stops growing at the configured ceiling.</summary>
    [Fact]
    public void PenaltyFor_BeyondTheCeiling_IsCapped()
    {
        // Arrange
        CredentialAttemptLimits limits = new(Permits, _window, maximumPenaltyWindows: 8);

        // Act
        TimeSpan capped = limits.PenaltyFor(20);

        // Assert
        Assert.Equal(_window * 8, capped);
        Assert.Equal(limits.MaximumPenalty, capped);
    }

    /// <summary>Attempts outside the window no longer count, so an occasional typo never blocks.</summary>
    [Fact]
    public void Check_AfterTheWindowPasses_CountsAgainFromZero()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, TestTimeProvider time) = Build();
        limiter.RecordChargeableAttempt(Key);
        limiter.RecordChargeableAttempt(Key);

        // Act
        time.Advance(_window + TimeSpan.FromSeconds(1));

        for (int attempt = 0; attempt < Permits - 1; attempt++)
        {
            Assert.True(limiter.Check(Key).Allowed);
            limiter.RecordChargeableAttempt(Key);
        }

        // Assert — the two attempts from the previous window did not carry over.
        Assert.True(limiter.Check(Key).Allowed);
    }

    /// <summary>A success clears the history, so a user who mistyped and then signed in is not penalised.</summary>
    [Fact]
    public void Reset_AfterFailures_ClearsThem()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, _) = Build();

        for (int attempt = 0; attempt < Permits; attempt++)
        {
            limiter.RecordChargeableAttempt(Key);
        }

        Assert.False(limiter.Check(Key).Allowed);

        // Act
        limiter.Reset(Key);

        // Assert
        Assert.True(limiter.Check(Key).Allowed);
    }

    /// <summary>One key's failures never refuse another key — the partition is per account.</summary>
    [Fact]
    public void Check_ASecondKey_IsUnaffectedByTheFirst()
    {
        // Arrange
        (InMemoryCredentialAttemptLimiter limiter, _) = Build();

        for (int attempt = 0; attempt < Permits; attempt++)
        {
            limiter.RecordChargeableAttempt(Key);
        }

        // Act
        CredentialAttemptDecision other = limiter.Check("someone-else@example.com");

        // Assert
        Assert.False(limiter.Check(Key).Allowed);
        Assert.True(other.Allowed);
    }

    private static (InMemoryCredentialAttemptLimiter Limiter, TestTimeProvider Time) Build()
    {
        TestTimeProvider time = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        CredentialAttemptLimits limits = new(Permits, _window, maximumPenaltyWindows: 8);

        return (new InMemoryCredentialAttemptLimiter(limits, time), time);
    }

    /// <summary>A clock the test moves by hand.</summary>
    /// <remarks>
    /// Hand-written rather than `Microsoft.Extensions.TimeProvider.Testing`, which is a package this
    /// solution does not pin — adding one is a decision, not a convenience (ADR-0021).
    /// </remarks>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _nowUtc;

        internal TestTimeProvider(DateTimeOffset nowUtc) => _nowUtc = nowUtc;

        public override DateTimeOffset GetUtcNow() => _nowUtc;

        internal void Advance(TimeSpan by) => _nowUtc += by;
    }
}
