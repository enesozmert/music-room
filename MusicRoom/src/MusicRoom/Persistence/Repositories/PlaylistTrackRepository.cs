using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playlists.Queries.ListTracks;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for playlist tracks.
/// </summary>
/// <remarks>
/// This type owns the transaction boundary and the read; <see cref="PlaylistTrackWriter"/> owns what happens
/// inside it. The split keeps one file per responsibility and keeps both inside the file-length gate.
/// </remarks>
internal sealed class PlaylistTrackRepository : IPlaylistTrackRepository
{
    private readonly MusicRoomDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PlaylistTrackWriter _writer;

    /// <summary>Takes the scoped context and the transaction boundary.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <param name="unitOfWork">The transaction this repository's writes must share — ADR-0028.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public PlaylistTrackRepository(MusicRoomDbContext context, IUnitOfWork unitOfWork)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _writer = new PlaylistTrackWriter(context, new PlaylistTrackPositions(context));
    }

    /// <inheritdoc />
    public Task<TrackWriteResult> TryAddAsync(
        Guid playlistId,
        TrackPlacement track,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);

        return InTransactionAsync(
            token => _writer.AddAsync(playlistId, track, expectedVersion, nowUtc, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TrackWriteResult> TryMoveAsync(
        Guid playlistId,
        TrackMove move,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(move);

        return InTransactionAsync(
            token => _writer.MoveAsync(playlistId, move, expectedVersion, nowUtc, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TrackWriteResult> TryRemoveAsync(
        Guid playlistId,
        Guid playlistTrackId,
        uint expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        InTransactionAsync(
            token => _writer.RemoveAsync(playlistId, playlistTrackId, expectedVersion, nowUtc, token),
            cancellationToken);

    /// <inheritdoc />
    public async Task<PlaylistTrackPage> ListAsync(
        Guid playlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // The playlist's `xmin`, read in the same request, is the `ETag` the client sends back on its next
        // write. A writer between these two statements changes the version, and that caller's next write is
        // then the `412` it should be.
        uint version = await _context.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.Id == playlistId)
            .Select(playlist => playlist.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PlaylistTrackSummary> tracks = await _context.PlaylistTracks
            .AsNoTracking()
            .Where(track => track.PlaylistId == playlistId)
            .OrderBy(track => track.Position)
            .ThenBy(track => track.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(track => new PlaylistTrackSummary(
                track.Id,
                track.YoutubeVideoId,
                track.Position,
                track.AddedByUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PlaylistTrackPage(tracks, version);
    }

    /// <summary>Runs one write inside the request's transaction, opening one only if none is open.</summary>
    /// <remarks>
    /// An auditable command already has a transaction open — <c>AuditBehaviour</c> opened it so the audit row
    /// and the effect commit together. This repository joins it and must not commit what it did not begin.
    /// </remarks>
    private async Task<TrackWriteResult> InTransactionAsync(
        Func<CancellationToken, Task<TrackWriteResult>> write,
        CancellationToken cancellationToken)
    {
        bool owned = await _unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            TrackWriteResult result = await write(cancellationToken).ConfigureAwait(false);

            if (owned)
            {
                await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
            if (owned)
            {
                await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }
}
