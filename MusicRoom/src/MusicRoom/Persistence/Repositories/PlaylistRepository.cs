using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for playlists and their collaborators.
/// </summary>
/// <remarks>
/// Every write is keyed on the caller in the statement itself, so a playlist the caller may not touch simply
/// does not match. The soft-delete query filter on <see cref="Playlist"/> means none of these methods has to
/// remember to exclude deleted rows.
/// </remarks>
internal sealed class PlaylistRepository : IPlaylistRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public PlaylistRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task CreateAsync(Playlist playlist, CancellationToken cancellationToken)
    {
        _context.Playlists.Add(playlist);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlaylistAccess> FindAccessAsync(
        Guid playlistId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Id == playlistId)
            .Select(playlist => new
            {
                playlist.OwnerId,
                playlist.Visibility,
                playlist.Licence,
                Collaborator = _context.PlaylistCollaborators
                    .Where(collaborator => collaborator.PlaylistId == playlistId
                        && collaborator.UserId == userId)
                    .Select(collaborator => (bool?)collaborator.CanEdit)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return PlaylistAccess.None;
        }

        bool isOwner = row.OwnerId == userId;
        bool maySee = isOwner || row.Collaborator is not null || row.Visibility == Visibility.Public;

        // `Everyone` means anybody who may see it may write to it; `InvitedOnly` means a collaborator grant is
        // required. The owner always may.
        bool mayEdit = isOwner
            || (maySee && row.Licence == LicenceMode.Everyone)
            || row.Collaborator == true;

        return new PlaylistAccess(Exists: true, maySee, mayEdit && maySee, isOwner);
    }

    /// <inheritdoc />
    public async Task<uint> FindVersionAsync(Guid playlistId, CancellationToken cancellationToken) =>
        await _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Id == playlistId)
            .Select(playlist => playlist.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PlaylistDetail?> FindAsync(
        Guid playlistId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        PlaylistAccess access = await FindAccessAsync(playlistId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            return null;
        }

        return await _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Id == playlistId)
            .Select(playlist => new PlaylistDetail(
                playlist.Id,
                playlist.Name,
                playlist.Visibility,
                playlist.Licence,
                playlist.OwnerId,
                playlist.Version,
                access.MayEdit))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaylistSummary>> ListAsync(
        Guid userId,
        PlaylistScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await Scoped(scope, userId)
            .OrderByDescending(playlist => playlist.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(playlist => new PlaylistSummary(
                playlist.Id,
                playlist.Name,
                playlist.Visibility,
                playlist.OwnerId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PlaylistWriteOutcome> TryRenameAsync(
        Guid playlistId,
        string name,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // The token is part of the predicate, so a stale caller updates nothing: the mismatch is decided by the
        // database under its own row lock rather than by a read this code performed earlier.
        int updated = await _context.Playlists
            .Where(playlist => playlist.Id == playlistId && playlist.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(playlist => playlist.Name, name)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context would
                    // have stamped is stamped here — SDD §8.3.
                    .SetProperty(playlist => playlist.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated > 0)
        {
            return PlaylistWriteOutcome.Updated;
        }

        // Nothing matched: either the playlist is gone, or somebody changed it first. The caller has already
        // established that it exists and may be edited, so this is a mismatch.
        bool stillThere = await _context.Playlists
            .AsNoTracking()
            .AnyAsync(playlist => playlist.Id == playlistId, cancellationToken)
            .ConfigureAwait(false);

        return stillThere ? PlaylistWriteOutcome.VersionMismatch : PlaylistWriteOutcome.NotFound;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetLicenceAsync(
        Guid playlistId,
        Guid ownerId,
        PlaylistLicence licence,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(licence);

        int updated = await _context.Playlists
            .Where(playlist => playlist.Id == playlistId && playlist.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(playlist => playlist.Visibility, licence.Visibility)
                    .SetProperty(playlist => playlist.Licence, licence.Licence)
                    .SetProperty(playlist => playlist.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteAsync(
        Guid playlistId,
        Guid ownerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int deleted = await _context.Playlists
            .Where(playlist => playlist.Id == playlistId && playlist.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(playlist => playlist.DeletedAtUtc, nowUtc)
                    .SetProperty(playlist => playlist.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<AddCollaboratorOutcome> TryAddCollaboratorAsync(
        Guid playlistId,
        Guid ownerId,
        CollaboratorGrant grant,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);

        bool ownsIt = await _context.Playlists
            .AsNoTracking()
            .AnyAsync(
                playlist => playlist.Id == playlistId && playlist.OwnerId == ownerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!ownsIt)
        {
            return AddCollaboratorOutcome.NotFound;
        }

        // The owner is already the owner; naming them would create a second, weaker grant on the same person.
        if (grant.UserId == ownerId)
        {
            return AddCollaboratorOutcome.Conflict;
        }

        bool userExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == grant.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (!userExists)
        {
            return AddCollaboratorOutcome.NotFound;
        }

        return await InsertCollaboratorAsync(playlistId, grant, nowUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryRemoveCollaboratorAsync(
        Guid playlistId,
        Guid ownerId,
        Guid collaboratorId,
        CancellationToken cancellationToken)
    {
        // Keyed on the owner through a subquery, so a collaborator cannot remove anybody — including themselves,
        // which the contract does not offer as a route.
        int removed = await _context.PlaylistCollaborators
            .Where(collaborator => collaborator.PlaylistId == playlistId
                && collaborator.UserId == collaboratorId
                && _context.Playlists.Any(playlist =>
                    playlist.Id == playlistId && playlist.OwnerId == ownerId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollaboratorSummary>> ListCollaboratorsAsync(
        Guid playlistId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        PlaylistAccess access = await FindAccessAsync(playlistId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!access.MaySee)
        {
            return [];
        }

        return await _context.PlaylistCollaborators
            .AsNoTracking()
            .Where(collaborator => collaborator.PlaylistId == playlistId)
            .OrderBy(collaborator => collaborator.CreatedAtUtc)
            .Select(collaborator => new CollaboratorSummary(
                collaborator.UserId,
                _context.Users
                    .Where(user => user.Id == collaborator.UserId)
                    .Select(user => user.DisplayName)
                    .First(),
                collaborator.CanEdit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Which playlists one scope means for one caller.</summary>
    private IQueryable<Playlist> Scoped(PlaylistScope scope, Guid userId) => scope switch
    {
        PlaylistScope.Mine => _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.OwnerId == userId),

        PlaylistScope.Shared => _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.OwnerId != userId
                && _context.PlaylistCollaborators.Any(collaborator =>
                    collaborator.PlaylistId == playlist.Id && collaborator.UserId == userId)),

        _ => _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Visibility == Visibility.Public),
    };

    /// <summary>
    /// Writes the collaborator row. A duplicate is the unique index's answer, not a read-then-write guess.
    /// </summary>
    private async Task<AddCollaboratorOutcome> InsertCollaboratorAsync(
        Guid playlistId,
        CollaboratorGrant grant,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        PlaylistCollaborator collaborator = new()
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            UserId = grant.UserId,
            CanEdit = grant.CanEdit,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        _context.PlaylistCollaborators.Add(collaborator);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return AddCollaboratorOutcome.Added;
        }
        catch (DbUpdateException exception) when (IsDuplicateCollaborator(exception))
        {
            _context.Entry(collaborator).State = EntityState.Detached;

            return AddCollaboratorOutcome.Conflict;
        }
    }

    private static bool IsDuplicateCollaborator(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException postgresException
        && string.Equals(
            postgresException.SqlState,
            Npgsql.PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgresException.ConstraintName,
            "ux_playlist_collaborators_playlist_id_user_id",
            StringComparison.Ordinal);
}
