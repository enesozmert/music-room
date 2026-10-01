using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// Invitations and attendance — the two roster tables of an event.
/// </summary>
/// <remarks>
/// Its own type so <see cref="EventRepository"/> stays inside the file-length gate, and because the two tables
/// are the same shape: one row per person per event, a duplicate refused by a unique index rather than by a
/// read-then-write check (`ef-core-persistence`).
/// </remarks>
internal sealed class EventRosterWriter
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    public EventRosterWriter(MusicRoomDbContext context) => _context = context;

    /// <summary>Invites somebody, if the caller owns the event and the account exists.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="userId">Who to invite.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict, or no such event for this owner.</returns>
    public async Task<RosterWriteOutcome> TryInviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        bool ownsIt = await _context.Events
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == eventId && candidate.OwnerId == ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (!ownsIt)
        {
            return RosterWriteOutcome.NotFound;
        }

        // The owner is already the owner; an invitation would be a weaker grant on somebody who has the
        // strongest one.
        if (userId == ownerId)
        {
            return RosterWriteOutcome.Conflict;
        }

        bool userExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!userExists)
        {
            return RosterWriteOutcome.NotFound;
        }

        EventInvitation invitation = new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        return await InsertAsync(
                invitation,
                "ux_event_invitations_event_id_user_id",
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Withdraws an invitation, if the caller owns the event.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="userId">Whose invitation to withdraw.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    public async Task<bool> TryUninviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Keyed on the owner through a subquery, so an invitee cannot uninvite anybody.
        int removed = await _context.EventInvitations
            .Where(invitation => invitation.EventId == eventId
                && invitation.UserId == userId
                && _context.Events.Any(candidate =>
                    candidate.Id == eventId && candidate.OwnerId == ownerId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <summary>Records an attendance. The caller's own, and only after the `member` gate said yes.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, or a conflict when they are already on the roster.</returns>
    public async Task<RosterWriteOutcome> TryAttendAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        EventAttendance attendance = new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        return await InsertAsync(
                attendance,
                "ux_event_attendances_event_id_user_id",
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the caller's own attendance.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    public async Task<bool> TryLeaveAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        int removed = await _context.EventAttendances
            .Where(attendance => attendance.EventId == eventId && attendance.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <summary>Lists the invitations, for the owner.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The invitations, or an empty list when the caller does not own the event.</returns>
    public async Task<IReadOnlyList<InvitationSummary>> ListInvitationsAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        await _context.EventInvitations
            .AsNoTracking()
            .Where(invitation => invitation.EventId == eventId
                && _context.Events.Any(candidate =>
                    candidate.Id == eventId && candidate.OwnerId == ownerId))
            .OrderBy(invitation => invitation.CreatedAtUtc)
            .Select(invitation => new InvitationSummary(
                invitation.UserId,
                _context.Users
                    .Where(user => user.Id == invitation.UserId)
                    .Select(user => user.DisplayName)
                    .First()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Writes one roster row. A duplicate is the unique index's answer, not a read-then-write guess.
    /// </summary>
    private async Task<RosterWriteOutcome> InsertAsync<TRow>(
        TRow row,
        string uniqueIndexName,
        CancellationToken cancellationToken)
        where TRow : class
    {
        _context.Add(row);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return RosterWriteOutcome.Added;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception, uniqueIndexName))
        {
            _context.Entry(row).State = EntityState.Detached;

            return RosterWriteOutcome.Conflict;
        }
    }

    private static bool IsDuplicate(DbUpdateException exception, string uniqueIndexName) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(postgresException.ConstraintName, uniqueIndexName, StringComparison.Ordinal);
}
