namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The transaction boundary a command runs in — SPEC-008 escalation 2, owner decision of 2026-09-27.
/// </summary>
/// <remarks>
/// SPEC-007 escalation 1 deferred this abstraction and said the next multi-aggregate write would
/// reconsider it. The audit log is that write: the record and the effect it describes must commit
/// together, so the boundary has to be visible from outside the repository that does the work.
/// <para>
/// Declared here, implemented in <c>Persistence</c> — ADR-0028. It is deliberately not
/// <c>IAsyncDisposable</c>: the scoped <c>DbContext</c> owns the connection, and a behaviour that forgot
/// to commit must leave an uncommitted transaction rather than an ambiguous half-state.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Opens a transaction, or joins one that is already open.</summary>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when this caller opened the transaction and therefore owns it. False when one was already
    /// open: the caller must not commit or roll back what it did not begin.
    /// </returns>
    Task<bool> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Commits the transaction this caller opened.</summary>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back the transaction this caller opened.</summary>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the transaction is rolled back.</returns>
    Task RollbackAsync(CancellationToken cancellationToken);
}
