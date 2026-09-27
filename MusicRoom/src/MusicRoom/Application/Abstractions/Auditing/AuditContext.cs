namespace MusicRoom.Application.Abstractions.Auditing;

/// <summary>
/// The request-scoped implementation of <see cref="IAuditContext"/>.
/// </summary>
/// <remarks>
/// In <c>Application</c> rather than <c>Infrastructure</c>, deliberately: it holds no external resource,
/// only state that lives and dies with one request, and putting it behind a project reference would buy
/// nothing. ADR-0028's rule is about external concerns.
/// <para>
/// Not thread-safe, and it does not need to be: one request, one handler, one writer. A future handler that
/// fans out must set the outcome from the thread that owns the request.
/// </para>
/// </remarks>
public sealed class AuditContext : IAuditContext
{
    /// <inheritdoc />
    public AuditOutcome Outcome { get; private set; } = AuditOutcome.Completed;

    /// <inheritdoc />
    public Guid? UserId { get; private set; }

    /// <inheritdoc />
    public Guid? TargetId { get; private set; }

    /// <inheritdoc />
    public void SetOutcome(AuditOutcome outcome) => Outcome = outcome;

    /// <inheritdoc />
    public void SetUserId(Guid userId) => UserId = userId;

    /// <inheritdoc />
    public void SetTargetId(Guid targetId) => TargetId = targetId;
}
