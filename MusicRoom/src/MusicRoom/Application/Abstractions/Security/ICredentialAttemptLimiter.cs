namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// The per-account half of the credential rate limit — SDD §13.3, `authentication-authorization`.
/// </summary>
/// <remarks>
/// The per-IP half is middleware. This half lives here because the account a request names is known
/// only after the body is bound and validated, and the limiter that runs before model binding would
/// have to parse untrusted JSON to find it — SPEC-006 escalation 2, option B.
/// <para>
/// Declared here, implemented in <c>Infrastructure</c> — ADR-0028. The interface names neither the
/// store nor the backoff curve, so replacing either touches one project.
/// </para>
/// </remarks>
public interface ICredentialAttemptLimiter
{
    /// <summary>Asks whether a key may make a chargeable attempt now.</summary>
    /// <param name="key">What the limit is keyed on: the submitted address, already normalised by the
    /// caller. It is a partition key and nothing else — no implementation may log it.</param>
    /// <returns>The decision, carrying how long the caller must wait when it is refused.</returns>
    CredentialAttemptDecision Check(string key);

    /// <summary>
    /// Charges one attempt against a key.
    /// </summary>
    /// <param name="key">The same key <see cref="Check"/> was asked about.</param>
    /// <remarks>
    /// Login charges only a *failed* attempt, because a correct password is not abuse. Registration and
    /// verification resend charge every attempt, because each one costs a write and, once O17 lands a
    /// real sender, a mail — the cost is in the attempt, not in its outcome.
    /// </remarks>
    void RecordChargeableAttempt(string key);

    /// <summary>Clears a key's history, which a successful credential use does.</summary>
    /// <param name="key">The key to forget.</param>
    void Reset(string key);
}

/// <summary>
/// What <see cref="ICredentialAttemptLimiter.Check"/> decided.
/// </summary>
/// <param name="Allowed">True when the attempt may proceed.</param>
/// <param name="RetryAfter">How long the caller must wait. <see cref="TimeSpan.Zero"/> when allowed.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as this method's return
/// value, and SPEC-006's Files table is the contract for what this change may create.
/// </remarks>
public sealed record CredentialAttemptDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>
/// Thrown by a handler whose account key is out of attempts, and turned into `429` by the endpoint.
/// </summary>
/// <remarks>
/// An exception rather than an outcome on three different return types: registration and resend answer
/// <c>Task</c>, login answers a token pair, and all three must produce the same `429` with the same
/// header. The message carries no address and no count.
/// </remarks>
public sealed class CredentialAttemptThrottledException : Exception
{
    /// <summary>Builds the exception with the wait the limiter reported.</summary>
    /// <param name="retryAfter">What the endpoint puts in <c>Retry-After</c>.</param>
    public CredentialAttemptThrottledException(TimeSpan retryAfter)
        : base("Too many attempts for this account. Retry later.") => RetryAfter = retryAfter;

    /// <summary>Required by CA1032; prefer the constructor that carries the wait.</summary>
    public CredentialAttemptThrottledException()
        : this(TimeSpan.Zero)
    {
    }

    /// <summary>Required by CA1032.</summary>
    /// <param name="message">The message.</param>
    public CredentialAttemptThrottledException(string message)
        : base(message) => RetryAfter = TimeSpan.Zero;

    /// <summary>Required by CA1032.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CredentialAttemptThrottledException(string message, Exception innerException)
        : base(message, innerException) => RetryAfter = TimeSpan.Zero;

    /// <summary>How long the caller must wait before the next attempt is accepted.</summary>
    public TimeSpan RetryAfter { get; }
}
