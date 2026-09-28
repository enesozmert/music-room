using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence;

/// <summary>
/// The EF Core transaction boundary, over the request's scoped context.
/// </summary>
/// <remarks>
/// <see cref="BeginAsync"/> is ambient-aware: EF Core refuses a second
/// <c>BeginTransactionAsync</c> on a context that already has one, and the repository methods written by
/// SPEC-003 through SPEC-007 open their own. Those methods now ask this type instead, so whichever caller
/// arrives first owns the transaction and the inner ones enlist.
/// </remarks>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public UnitOfWork(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<bool> BeginAsync(CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is not null)
        {
            return false;
        }

        await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken) =>
        _context.Database.CurrentTransaction is null
            ? Task.CompletedTask
            : _context.Database.CommitTransactionAsync(cancellationToken);

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken) =>
        _context.Database.CurrentTransaction is null
            ? Task.CompletedTask
            : _context.Database.RollbackTransactionAsync(cancellationToken);
}
