using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Auditing;

/// <summary>
/// Writes audit records to <c>audit_records</c> through the request's scoped context.
/// </summary>
/// <remarks>
/// No transaction of its own: it writes inside whatever the caller opened, which is what makes the record
/// and the effect it describes commit together — SPEC-008 escalations 1 and 2.
/// </remarks>
internal sealed class AuditSink : IAuditSink
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public AuditSink(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        _context.AuditRecords.Add(record);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
