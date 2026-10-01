using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Playlists.Queries.ListCollaborators;

/// <summary>
/// Lists the people named on a playlist — `GET /api/v1/playlists/{playlistId}/collaborators`.
/// </summary>
/// <param name="PlaylistId">The playlist.</param>
public sealed record ListCollaboratorsQuery(Guid PlaylistId) : IRequest<IReadOnlyList<CollaboratorSummary>>;
