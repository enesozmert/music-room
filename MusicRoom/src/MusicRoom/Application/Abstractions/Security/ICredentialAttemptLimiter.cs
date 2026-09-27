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
/// A refusal to spend another attempt, and how long the caller must wait.
/// </summary>
/// <param name="RetryAfter">What the endpoint puts in <c>Retry-After</c>.</param>
/// <remarks>
/// A value rather than an exception — SPEC-009 escalation 2, owner decision of 2026-09-28. The exception it
/// replaces was thrown on a path the system expects, and SPEC-008's fail-closed behaviour rolled the audit
/// record back with it, so the loudest event in the system left no row. A handler now returns this and the
/// endpoint maps it, which keeps the record on the ordinary success path of the pipeline.
/// <para>
/// Declared beside the limiter rather than in its own file: it exists only as the shape of a refusal, and
/// SPEC-009's Files table is the contract for what this change may create.
/// </para>
/// </remarks>
public sealed record CredentialThrottle(TimeSpan RetryAfter);
