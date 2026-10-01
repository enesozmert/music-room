using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core paths for the venue codes of ADR-0014.
/// </summary>
/// <remarks>
/// Spending is arbitrated by <c>ux_event_code_redemptions_event_code_id_user_id</c>, not by a read: two parallel
/// votes carrying the same code cannot both spend it, because the second insert violates the index. A check in
/// C# would be a race with a comment attached.
/// <para>
/// Neither the code nor the digest is logged anywhere, and a failed spend says only that it failed.
/// </para>
/// </remarks>
internal sealed class EventCodeRepository : IEventCodeRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public EventCodeRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task IssueAsync(
        Guid eventId,
        string codeHash,
        DateTime expiresAtUtc,
        Guid issuedByUserId,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        _context.EventCodes.Add(new EventCode
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            CodeHash = codeHash,
            ExpiresAtUtc = expiresAtUtc,
            IssuedByUserId = issuedByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TrySpendAsync(
        Guid eventId,
        string codeHash,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // One indexed read on the digest, narrowed to this event and to codes that have not expired by server
        // time. An unknown code, another event's code and an expired code are the same answer here, and the same
        // answer to the caller.
        Guid codeId = await _context.EventCodes
            .AsNoTracking()
            .Where(code => code.CodeHash == codeHash
                && code.EventId == eventId
                && code.ExpiresAtUtc > nowUtc)
            .Select(code => code.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (codeId == Guid.Empty)
        {
            return false;
        }

        EventCodeRedemption redemption = new()
        {
            Id = Guid.NewGuid(),
            EventCodeId = codeId,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.EventCodeRedemptions.Add(redemption);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException exception) when (IsAlreadySpent(exception))
        {
            _context.Entry(redemption).State = EntityState.Detached;

            // This account has already used this code. Refused, with the same answer every other code failure
            // gets — SPEC-018 decision 6.
            return false;
        }
    }

    private static bool IsAlreadySpent(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgresException.ConstraintName,
            "ux_event_code_redemptions_event_code_id_user_id",
            StringComparison.Ordinal);
}
