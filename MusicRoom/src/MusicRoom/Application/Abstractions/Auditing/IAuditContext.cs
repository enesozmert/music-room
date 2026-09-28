namespace MusicRoom.Application.Abstractions.Auditing;

/// <summary>
/// What a handler contributes to its own audit record — SPEC-009 escalation 1.
/// </summary>
/// <remarks>
/// The outcome is known inside the handler; the record is built outside it, in the pipeline behaviour. This
/// carries the fact across. It is request-scoped and written by exactly one handler per request, and last
/// writer wins: a handler that reads, then refuses, states the refusal last.
/// <para>
/// A handler that says nothing is not an error. The behaviour then records
/// <see cref="AuditOutcome.Completed"/> with no account, which is still a true statement about a command
/// that ran to completion.
/// </para>
/// </remarks>
public interface IAuditContext
{
    /// <summary>What became of the action.</summary>
    AuditOutcome Outcome { get; }

    /// <summary>The account the action was for, once the handler knows it.</summary>
    Guid? UserId { get; }

    /// <summary>The thing acted on, once the handler knows it.</summary>
    Guid? TargetId { get; }

    /// <summary>States what became of the action.</summary>
    /// <param name="outcome">The outcome to record.</param>
    void SetOutcome(AuditOutcome outcome);

    /// <summary>
    /// Names the account the action was for.
    /// </summary>
    /// <param name="userId">The account id. Never an address: the record stores identifiers only (§13.5).</param>
    void SetUserId(Guid userId);

    /// <summary>Names the thing acted on.</summary>
    /// <param name="targetId">The target's id.</param>
    void SetTargetId(Guid targetId);
}

/// <summary>
/// The vocabulary the <c>outcome</c> column uses.
/// </summary>
/// <remarks>
/// A type rather than loose strings: the same value is written by seven handlers and asserted by two test
/// suites, and a typo in one of nine places would be a row nobody ever finds.
/// </remarks>
public enum AuditOutcome
{
    /// <summary>The action ran to completion. The default when a handler says nothing.</summary>
    Completed = 0,

    /// <summary>The action was refused: wrong credential, unknown or already-spent token.</summary>
    Refused = 1,

    /// <summary>The action was refused because the account is out of attempts — SDD §13.3.</summary>
    Throttled = 2,

    /// <summary>The credential existed and its window had passed.</summary>
    Expired = 3,

    /// <summary>
    /// An already-rotated refresh token was presented, so the whole family was revoked — SDD §13.1. The
    /// loudest row in the table: it means a refresh token leaked.
    /// </summary>
    ReuseDetected = 4,
}
