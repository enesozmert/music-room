using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for devices and their control grants.
/// </summary>
/// <remarks>
/// Every write is keyed on the caller inside the statement, so a device the caller does not own simply does not
/// match. The claim also carries the `xmin` the caller read, which is what makes the loser of a race lose
/// visibly instead of silently overwriting the winner.
/// </remarks>
internal sealed class DeviceRepository : IDeviceRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public DeviceRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<DeviceWriteOutcome> TryRegisterAsync(
        Device device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        _context.Devices.Add(device);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return DeviceWriteOutcome.Written;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            _context.Entry(device).State = EntityState.Detached;

            // Already registered. The index decided it, which is what makes the `409` a fact rather than a
            // read-then-write guess.
            return DeviceWriteOutcome.Conflict;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken) =>
        await _context.Devices
            .AsNoTracking()
            .Where(device => device.OwnerId == ownerId)
            .OrderByDescending(device => device.CreatedAtUtc)
            .Select(device => new DeviceSummary(
                device.Id,
                device.Platform,
                device.Model,
                device.AppVersion,
                device.ControllerUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TryRemoveAsync(
        Guid deviceId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        int removed = await _context.Devices
            .Where(device => device.Id == deviceId && device.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<DeviceControl?> FindControlAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceId
                && (device.OwnerId == userId || device.ControllerUserId == userId))
            .Select(device => new DeviceControl(
                device.Id,
                device.OwnerId,

                // With no grant the controller **is** the owner, which is the question this endpoint was asked.
                // The column stays null, which answers a different question — SPEC-020 decision 2.
                device.ControllerUserId ?? device.OwnerId,
                device.ControllerUserId != null,
                device.Version))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<DeviceGrantResult> TryGrantControlAsync(
        Guid deviceId,
        Guid ownerId,
        Guid granteeUserId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        // Read before the write and under the same token: whoever held control at this version is who the
        // takeover notice belongs to. If the token turns out to be stale the update below matches nothing and
        // this value is discarded with it, so it can never name somebody a concurrent grant has displaced —
        // SPEC-022 decision 3.
        Guid? previousController = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceId
                && device.OwnerId == ownerId
                && device.Version == expectedVersion)
            .Select(device => device.ControllerUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The owner and the token are both in the predicate, so a caller who does not own the device and a
        // caller holding a stale token each update nothing — and the database decides which, under its own row
        // lock, rather than this code deciding from a read it performed earlier.
        int updated = await _context.Devices
            .Where(device => device.Id == deviceId
                && device.OwnerId == ownerId
                && device.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(device => device.ControllerUserId, granteeUserId)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context would
                    // have stamped is stamped here — SDD §8.3.
                    .SetProperty(device => device.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated > 0)
        {
            return new DeviceGrantResult(DeviceWriteOutcome.Written, previousController);
        }

        bool ownsIt = await _context.Devices
            .AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.OwnerId == ownerId, cancellationToken)
            .ConfigureAwait(false);

        return ownsIt ? DeviceGrantResult.Stale : DeviceGrantResult.NotFound;
    }

    /// <inheritdoc />
    public async Task<bool> TryReleaseControlAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        // Both permitted callers in one predicate: the owner revoking, and the controller handing it back. A
        // device that is already under owner control matches nothing and answers `404`, which is honest —
        // there was no grant to release.
        int updated = await _context.Devices
            .Where(device => device.Id == deviceId
                && device.ControllerUserId != null
                && (device.OwnerId == userId || device.ControllerUserId == userId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(device => device.ControllerUserId, (Guid?)null)
                    .SetProperty(device => device.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ControlGrantSummary>> ListGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _context.Devices
            .AsNoTracking()
            .Where(device => device.ControllerUserId == userId)
            .OrderByDescending(device => device.UpdatedAtUtc)
            .Select(device => new ControlGrantSummary(
                device.Id,
                device.OwnerId,
                _context.Users
                    .Where(user => user.Id == device.OwnerId)
                    .Select(user => user.DisplayName)
                    .First(),
                device.Platform,
                device.Model))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgresException.ConstraintName,
            "ux_devices_owner_id_platform_model",
            StringComparison.Ordinal);
}
