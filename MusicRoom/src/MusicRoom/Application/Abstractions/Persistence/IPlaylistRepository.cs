using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// Playlists and the people named on them — <c>docs/api/ENDPOINTS.md</c> §5.
/// </summary>
/// <remarks>
/// Every member takes the caller and enforces its own gate **in the statement**, so a playlist the caller may
/// not touch does not match rather than being found and then refused. That is what makes the contract's
/// "`404`, never `403`" true without a second check anybody could forget.
/// </remarks>
public interface IPlaylistRepository
{
    /// <summary>Creates a playlist owned by the caller.</summary>
    /// <param name="playlist">The row to write.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when it is written.</returns>
    Task CreateAsync(Playlist playlist, CancellationToken cancellationToken);

    /// <summary>
    /// What one caller may do with one playlist.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The rights. One read rather than three, because every route needs the same answer and asking three times
    /// invites the three answers to disagree.
    /// </returns>
    Task<PlaylistAccess> FindAccessAsync(Guid playlistId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The playlist's current `xmin`, or zero when there is no such playlist.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The version a broadcast carries — SPEC-016 decision 2.</returns>
    /// <remarks>
    /// A collaborator change does not touch the playlist row, so its broadcast reports the version the
    /// playlist already has. A listener that compares it against what it holds learns that the track list is
    /// unchanged, which is true.
    /// </remarks>
    Task<uint> FindVersionAsync(Guid playlistId, CancellationToken cancellationToken);

    /// <summary>Reads one playlist, if the caller may see it.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The playlist and its concurrency token, or null when the caller may not see it.</returns>
    Task<PlaylistDetail?> FindAsync(Guid playlistId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Lists the playlists the caller may see, in one scope.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="scope">`mine`, `shared` or `public`.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per playlist.</returns>
    Task<IReadOnlyList<PlaylistSummary>> ListAsync(
        Guid userId,
        PlaylistScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renames a playlist, if the caller may edit it and holds the current token.
    /// </summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="name">The new name.</param>
    /// <param name="expectedVersion">The token the caller last read, from `If-Match`.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// <see cref="PlaylistWriteOutcome.Updated"/>, or <see cref="PlaylistWriteOutcome.VersionMismatch"/> when
    /// somebody else changed the row first, which the endpoint answers `412`.
    /// </returns>
    Task<PlaylistWriteOutcome> TryRenameAsync(
        Guid playlistId,
        string name,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Sets visibility and licence. Owner only, and no `If-Match`: the contract lists none.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="licence">The new visibility and licence.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the owner's playlist was updated.</returns>
    Task<bool> TrySetLicenceAsync(
        Guid playlistId,
        Guid ownerId,
        PlaylistLicence licence,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes a playlist. Owner only.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="nowUtc">The instant it is stamped deleted.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the owner's playlist was deleted.</returns>
    Task<bool> TryDeleteAsync(
        Guid playlistId,
        Guid ownerId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Names somebody on a playlist. Owner only.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="grant">Who is being named, and whether they may write.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome: added, a conflict, or no such playlist for this owner.</returns>
    Task<AddCollaboratorOutcome> TryAddCollaboratorAsync(
        Guid playlistId,
        Guid ownerId,
        CollaboratorGrant grant,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Removes somebody from a playlist. Owner only.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="collaboratorId">The account to remove.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a collaborator of the owner's playlist was removed.</returns>
    Task<bool> TryRemoveCollaboratorAsync(
        Guid playlistId,
        Guid ownerId,
        Guid collaboratorId,
        CancellationToken cancellationToken);

    /// <summary>Lists the people named on a playlist, if the caller may see it.</summary>
    /// <param name="playlistId">The playlist.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per collaborator, or an empty list when the caller may not see the playlist.</returns>
    Task<IReadOnlyList<CollaboratorSummary>> ListCollaboratorsAsync(
        Guid playlistId,
        Guid userId,
        CancellationToken cancellationToken);
}

/// <summary>A playlist's visibility and licence, set together because the contract's `PUT` sets both.</summary>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it. `LocationAndTimeRestricted` is refused for a playlist.</param>
public sealed record PlaylistLicence(Visibility Visibility, LicenceMode Licence);

/// <summary>Who is being named on a playlist, and what they may do.</summary>
/// <param name="UserId">The account to name.</param>
/// <param name="CanEdit">Whether they may write to it, not merely read it.</param>
public sealed record CollaboratorGrant(Guid UserId, bool CanEdit);

/// <summary>What one caller may do with one playlist.</summary>
/// <param name="Exists">Whether a playlist with that id exists and is not deleted.</param>
/// <param name="MaySee">Owner, collaborator, or the playlist is public.</param>
/// <param name="MayEdit">Owner, or a collaborator whose grant allows it, or the licence is `Everyone` and the
/// caller may see it.</param>
/// <param name="IsOwner">Whether the caller owns it.</param>
/// <remarks>
/// Declared beside the interface, like every other read shape in this codebase: it exists as one method's
/// return value.
/// </remarks>
public sealed record PlaylistAccess(bool Exists, bool MaySee, bool MayEdit, bool IsOwner)
{
    /// <summary>What a caller sees for a playlist that does not exist, or that they may not see at all.</summary>
    public static PlaylistAccess None { get; } = new(false, false, false, false);
}

/// <summary>One playlist as its reader sees it, with the token an `ETag` carries.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Name">Its name.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it.</param>
/// <param name="OwnerId">Its owner.</param>
/// <param name="Version">PostgreSQL's `xmin`, which the endpoint returns as the `ETag`.</param>
/// <param name="MayEdit">Whether this reader may write to it, so a client can hide an editor it cannot use.</param>
public sealed record PlaylistDetail(
    Guid PlaylistId,
    string Name,
    Visibility Visibility,
    LicenceMode Licence,
    Guid OwnerId,
    uint Version,
    bool MayEdit);

/// <summary>One playlist in a list.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Name">Its name.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="OwnerId">Its owner.</param>
public sealed record PlaylistSummary(Guid PlaylistId, string Name, Visibility Visibility, Guid OwnerId);

/// <summary>One person named on a playlist.</summary>
/// <param name="UserId">Their account.</param>
/// <param name="DisplayName">Their handle.</param>
/// <param name="CanEdit">Whether they may write to it.</param>
public sealed record CollaboratorSummary(Guid UserId, string DisplayName, bool CanEdit);

/// <summary>Which playlists a list request wants.</summary>
public enum PlaylistScope
{
    /// <summary>Owned by the caller.</summary>
    Mine = 0,

    /// <summary>Somebody else's, with the caller named on it.</summary>
    Shared = 1,

    /// <summary>Public, whoever owns them.</summary>
    Public = 2,
}

/// <summary>What a guarded write did.</summary>
public enum PlaylistWriteOutcome
{
    /// <summary>No playlist matched, or the caller may not write to it.</summary>
    NotFound = 0,

    /// <summary>Written.</summary>
    Updated = 1,

    /// <summary>Somebody changed the row after the caller read it — the endpoint answers `412`.</summary>
    VersionMismatch = 2,
}

/// <summary>What naming a collaborator did.</summary>
public enum AddCollaboratorOutcome
{
    /// <summary>No such playlist for this owner.</summary>
    NotFound = 0,

    /// <summary>Named.</summary>
    Added = 1,

    /// <summary>Already named, or the owner naming themselves.</summary>
    Conflict = 2,
}
