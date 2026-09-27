using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Auditing;

/// <summary>
/// Where an audit record is written — SDD §13.4.
/// </summary>
/// <remarks>
/// Declared here, implemented in <c>Persistence</c> — ADR-0028. The interface names no table and no
/// engine, and it deliberately has no read member: nothing in the application reads the audit log, and an
/// audit trail readable over the API is an authorisation surface of its own.
/// </remarks>
public interface IAuditSink
{
    /// <summary>Writes one record, in the caller's transaction.</summary>
    /// <param name="record">The record to write.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    /// <remarks>
    /// It does not commit. The transaction belongs to whoever opened it, which is how the record and the
    /// effect it describes commit together — SPEC-008 escalations 1 and 2.
    /// </remarks>
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken);
}
