using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playlists.Commands.AddCollaborator;
using MusicRoom.Application.Features.Playlists.Commands.RemoveCollaborator;
using MusicRoom.Application.Features.Playlists.Queries.ListCollaborators;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The collaborator routes of a playlist — <c>docs/api/ENDPOINTS.md</c> §5.
/// </summary>
/// <remarks>
/// Its own class rather than three more members on <see cref="PlaylistEndpoints"/>, which would put that file
/// past the 300-line limit the root .editorconfig enforces. Listing is the `member` gate; adding and removing
/// are owner-only.
/// </remarks>
internal static class PlaylistCollaboratorEndpoints
{
    /// <summary>Maps the three routes onto the playlist group.</summary>
    /// <param name="group">The <c>/api/v1/playlists</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapList(group);
        MapAdd(group);
        MapRemove(group);
    }

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/{playlistId:guid}/collaborators", ListAsync)
            .RequireAuthorization()
            .WithName("GetPlaylistCollaborators")
            .WithSummary("List who is named on a playlist")
            .WithDescription(
                "Anybody who may see the playlist may see who is named on it. A caller who may not see the "
                + "playlist answers 404, not an empty list, because an empty list would confirm it exists.")
            .Produces<IReadOnlyList<CollaboratorSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapAdd(RouteGroupBuilder group) =>
        group.MapPost("/{playlistId:guid}/collaborators", AddAsync)
            .RequireAuthorization()
            .WithName("PostPlaylistCollaborator")
            .WithSummary("Name somebody on a playlist")
            .WithDescription(
                "Owner only. 409 when they are already named, and when the owner names themselves — they "
                + "already hold every right a collaborator grant could give.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

    private static void MapRemove(RouteGroupBuilder group) =>
        group.MapDelete("/{playlistId:guid}/collaborators/{userId:guid}", RemoveAsync)
            .RequireAuthorization()
            .WithName("DeletePlaylistCollaborator")
            .WithSummary("Remove somebody from a playlist")
            .WithDescription("Owner only. 404 when there is no such grant on a playlist this caller owns.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> ListAsync(
        Guid playlistId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CollaboratorSummary> collaborators = await sender.Send(
            new ListCollaboratorsQuery(playlistId),
            cancellationToken);

        // An empty list and "you may not see this" are different answers, and the query returns an empty list
        // for both — so the endpoint asks again for the playlist itself to tell them apart.
        return collaborators.Count > 0
            ? Results.Ok(collaborators)
            : await EmptyOrNotFoundAsync(playlistId, sender, cancellationToken);
    }

    private static async Task<IResult> AddAsync(
        Guid playlistId,
        CollaboratorCreation request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        AddCollaboratorOutcome outcome = await sender.Send(
            new AddCollaboratorCommand(playlistId, request.UserId, request.CanEdit),
            cancellationToken);

        return outcome switch
        {
            AddCollaboratorOutcome.Added => Results.Created(
                $"/api/v1/playlists/{playlistId}/collaborators/{request.UserId}",
                value: null),
            AddCollaboratorOutcome.Conflict => Results.Conflict(),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> RemoveAsync(
        Guid playlistId,
        Guid userId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new RemoveCollaboratorCommand(playlistId, userId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    /// <summary>
    /// Tells "nobody is named" apart from "you may not see this playlist", which the query cannot.
    /// </summary>
    private static async Task<IResult> EmptyOrNotFoundAsync(
        Guid playlistId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        PlaylistDetail? playlist = await sender.Send(
            new Application.Features.Playlists.Queries.GetPlaylist.GetPlaylistQuery(playlistId),
            cancellationToken);

        return playlist is null ? Results.NotFound() : Results.Ok(Array.Empty<CollaboratorSummary>());
    }
}

/// <summary>The collaborator payload — <c>docs/api/ENDPOINTS.md</c> §5.</summary>
/// <param name="UserId">The account to name.</param>
/// <param name="CanEdit">Whether they may write to it.</param>
internal sealed record CollaboratorCreation(Guid UserId, bool CanEdit);
