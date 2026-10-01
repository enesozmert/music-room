using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for the event aggregate.
/// </summary>
/// <remarks>
/// Every write is keyed on the owner inside the statement, so an event the caller does not own simply does not
/// match and the endpoint answers `404`. The roster lives in <see cref="EventRosterWriter"/>; this type keeps
/// the aggregate and the gate read.
/// </remarks>
internal sealed class EventRepository : IEventRepository
{
    private readonly MusicRoomDbContext _context;
    private readonly EventRosterWriter _roster;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public EventRepository(MusicRoomDbContext context)
    {
        _context = context;
        _roster = new EventRosterWriter(context);
    }

    /// <inheritdoc />
    public async Task CreateAsync(Event newEvent, CancellationToken cancellationToken)
    {
        _context.Events.Add(newEvent);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EventAccess> FindAccessAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var row = await _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.Id == eventId)
            .Select(candidate => new
            {
                candidate.OwnerId,
                candidate.Visibility,
                candidate.Licence,
                candidate.StartsAtUtc,
                candidate.EndsAtUtc,
                Invited = _context.EventInvitations.Any(invitation =>
                    invitation.EventId == eventId && invitation.UserId == userId),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return EventAccess.None;
        }

        bool isOwner = row.OwnerId == userId;
        bool maySee = isOwner || row.Invited || row.Visibility == Visibility.Public;

        // Server time, never a client's — ADR-0014.
        bool inWindow = nowUtc >= row.StartsAtUtc && nowUtc <= row.EndsAtUtc;

        return new EventAccess(
            Exists: true,
            maySee,
            MayVote(row.Licence, maySee, row.Invited, isOwner, inWindow),
            isOwner,
            inWindow);
    }

    /// <inheritdoc />
    public async Task<EventPresence?> FindPresenceAsync(
        Guid eventId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        await _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.Id == eventId)
            .Select(candidate => new EventPresence(
                candidate.Licence,
                candidate.PresenceProof,
                candidate.GeofenceLatitude == null
                    || candidate.GeofenceLongitude == null
                    || candidate.GeofenceRadiusMetres == null
                        ? null
                        : new PresencePolicy(
                            candidate.GeofenceLatitude.Value,
                            candidate.GeofenceLongitude.Value,
                            candidate.GeofenceRadiusMetres.Value),
                nowUtc >= candidate.StartsAtUtc && nowUtc <= candidate.EndsAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<EventDetail?> FindAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        EventAccess access = await FindAccessAsync(eventId, userId, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            return null;
        }

        return await _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.Id == eventId)
            .Select(candidate => new EventDetail(
                candidate.Id,
                candidate.Name,
                candidate.Visibility,
                candidate.Licence,
                candidate.StartsAtUtc,
                candidate.EndsAtUtc,
                candidate.OwnerId,
                candidate.Version,
                candidate.GeofenceLatitude == null
                    || candidate.GeofenceLongitude == null
                    || candidate.GeofenceRadiusMetres == null
                        ? null
                        : new PresencePolicy(
                            candidate.GeofenceLatitude.Value,
                            candidate.GeofenceLongitude.Value,
                            candidate.GeofenceRadiusMetres.Value)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventSummary>> ListAsync(
        Guid userId,
        EventScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await Scoped(scope, userId)
            .OrderBy(candidate => candidate.StartsAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(candidate => new EventSummary(
                candidate.Id,
                candidate.Name,
                candidate.Visibility,
                candidate.StartsAtUtc,
                candidate.EndsAtUtc,
                candidate.OwnerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<EventWriteOutcome> TryUpdateAsync(
        Guid eventId,
        Guid ownerId,
        EventMetadata metadata,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        DateTime nowUtc = DateTime.UtcNow;

        // Owner and token both in the predicate, so a stale caller and a caller who does not own the event
        // each update nothing — and the database decides which, under its own row lock.
        int updated = await _context.Events
            .Where(candidate => candidate.Id == eventId
                && candidate.OwnerId == ownerId
                && candidate.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Name, metadata.Name)
                    .SetProperty(candidate => candidate.StartsAtUtc, metadata.StartsAtUtc)
                    .SetProperty(candidate => candidate.EndsAtUtc, metadata.EndsAtUtc)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context
                    // would have stamped is stamped here — SDD §8.3.
                    .SetProperty(candidate => candidate.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated > 0)
        {
            return EventWriteOutcome.Updated;
        }

        // Nothing matched. If the caller still owns an event with this id, the token is what failed; otherwise
        // there is nothing here for them, which is the same `404` a stranger gets.
        bool ownsIt = await _context.Events
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == eventId && candidate.OwnerId == ownerId,
                cancellationToken)
            .ConfigureAwait(false);

        return ownsIt ? EventWriteOutcome.VersionMismatch : EventWriteOutcome.NotFound;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetLicenceAsync(
        Guid eventId,
        Guid ownerId,
        EventLicence licence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(licence);

        DateTime nowUtc = DateTime.UtcNow;

        int updated = await _context.Events
            .Where(candidate => candidate.Id == eventId && candidate.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Visibility, licence.Visibility)
                    .SetProperty(candidate => candidate.Licence, licence.Licence)
                    .SetProperty(candidate => candidate.PresenceProof, licence.Proof)
                    .SetProperty(candidate => candidate.GeofenceLatitude, licence.Geofence?.Latitude)
                    .SetProperty(candidate => candidate.GeofenceLongitude, licence.Geofence?.Longitude)
                    .SetProperty(candidate => candidate.GeofenceRadiusMetres, licence.Geofence?.RadiusMetres)
                    .SetProperty(candidate => candidate.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteAsync(
        Guid eventId,
        Guid ownerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int deleted = await _context.Events
            .Where(candidate => candidate.Id == eventId && candidate.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.DeletedAtUtc, nowUtc)
                    .SetProperty(candidate => candidate.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <inheritdoc />
    public Task<RosterWriteOutcome> TryInviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _roster.TryInviteAsync(eventId, ownerId, userId, nowUtc, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryUninviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        CancellationToken cancellationToken) =>
        _roster.TryUninviteAsync(eventId, ownerId, userId, cancellationToken);

    /// <inheritdoc />
    public Task<RosterWriteOutcome> TryAttendAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _roster.TryAttendAsync(eventId, userId, nowUtc, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryLeaveAsync(Guid eventId, Guid userId, CancellationToken cancellationToken) =>
        _roster.TryLeaveAsync(eventId, userId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<InvitationSummary>> ListInvitationsAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        _roster.ListInvitationsAsync(eventId, ownerId, cancellationToken);

    /// <summary>
    /// The `licence` gate as far as the stored policy decides it.
    /// </summary>
    /// <remarks>
    /// `LocationAndTimeRestricted` additionally needs the presence proof of ADR-0014, which the vote itself
    /// checks — the second unit of slice S6 owns the only consumer. What this answers for that mode is the
    /// part that is decidable from the row: the caller may see the event and server time is inside the window.
    /// </remarks>
    private static bool MayVote(
        LicenceMode licence,
        bool maySee,
        bool invited,
        bool isOwner,
        bool inWindow)
    {
        if (isOwner)
        {
            return true;
        }

        if (!maySee)
        {
            return false;
        }

        return licence switch
        {
            LicenceMode.Everyone => true,
            LicenceMode.InvitedOnly => invited,
            _ => inWindow,
        };
    }

    /// <summary>Which events one scope means for one caller.</summary>
    private IQueryable<Event> Scoped(EventScope scope, Guid userId) => scope switch
    {
        EventScope.Mine => _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.OwnerId == userId),

        EventScope.Invited => _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.OwnerId != userId
                && _context.EventInvitations.Any(invitation =>
                    invitation.EventId == candidate.Id && invitation.UserId == userId)),

        _ => _context.Events
            .AsNoTracking()
            .Where(candidate => candidate.Visibility == Visibility.Public),
    };
}
