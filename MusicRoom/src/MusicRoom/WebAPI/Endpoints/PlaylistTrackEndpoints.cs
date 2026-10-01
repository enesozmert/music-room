using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playlists.Commands.AddTrack;
using MusicRoom.Application.Features.Playlists.Commands.MoveTrack;
using MusicRoom.Application.Features.Playlists.Commands.RemoveTrack;
using MusicRoom.Application.Features.Playlists.Queries.ListTracks;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The track routes of a playlist — <c>docs/api/ENDPOINTS.md</c> §5.
/// </summary>
/// <remarks>
/// A collection has no `ETag` of its own, so these routes use the **playlist's** <c>xmin</c>: the `GET` returns
/// it, and every write sends it back as `If-Match` and is refused with `412` if somebody else moved first. The
/// three writes each update the playlist row inside their own transaction, which is what makes that token mean
/// "the track list you read" and not merely "the name you read".
/// <para>
/// Its own class rather than four more members on <see cref="PlaylistEndpoints"/>, which would put that file
/// past the 300-line limit the root .editorconfig enforces.
/// </para>
/// </remarks>
internal static class PlaylistTrackEndpoints
{
    /// <summary>The page size a client gets when it asks for none.</summary>
    internal const int DefaultPageSize = 50;

    /// <summary>Maps the four routes onto the playlist group.</summary>
    /// <param name="group">The <c>/api/v1/playlists</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapList(group);
        MapAdd(group);
        MapMove(group);
        MapRemove(group);
    }

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/{playlistId:guid}/tracks", ListAsync)
            .RequireAuthorization()
            .WithName("GetPlaylistTracks")
            .WithSummary("List a playlist's tracks in order")
            .WithDescription(
                "Anybody who may see the playlist may see its tracks. The response carries the ETag the client "
                + "sends back as If-Match when it adds, moves or removes one. A caller who may not see the "
                + "playlist answers 404, not an empty list, because an empty list would confirm it exists.")
            .Produces<IReadOnlyList<PlaylistTrackSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapAdd(RouteGroupBuilder group) =>
        group.MapPost("/{playlistId:guid}/tracks", AddAsync)
            .RequireAuthorization()
            .WithName("PostPlaylistTrack")
            .WithSummary("Add a track to a playlist")
            .WithDescription(
                "Requires If-Match carrying the ETag of the track list the client last read. Appends by "
                + "default; atHead or afterTrackId names a position. A playlist already holding its maximum "
                + "number of tracks answers 409. The same video may appear twice: a playlist that plays a "
                + "track twice is a playlist, not an error.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapMove(RouteGroupBuilder group) =>
        group.MapPatch("/{playlistId:guid}/tracks/{playlistTrackId:guid}", MoveAsync)
            .RequireAuthorization()
            .WithName("PatchPlaylistTrack")
            .WithSummary("Move a track within a playlist")
            .WithDescription(
                "Exactly one of afterTrackId or beforeTrackId. Requires If-Match: when two clients reorder the "
                + "same list at once exactly one is applied and the other is told 412 and reloads, which is "
                + "the whole point of versioning a collection.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapRemove(RouteGroupBuilder group) =>
        group.MapDelete("/{playlistId:guid}/tracks/{playlistTrackId:guid}", RemoveAsync)
            .RequireAuthorization()
            .WithName("DeletePlaylistTrack")
            .WithSummary("Remove a track from a playlist")
            .WithDescription(
                "Requires If-Match. Removing leaves the remaining order untouched: positions are sparse, so "
                + "the hole costs nothing and no other row is rewritten.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed);

    private static async Task<IResult> ListAsync(
        Guid playlistId,
        [AsParameters] TrackPaging paging,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        PlaylistTrackPage? tracks = await sender.Send(
            new ListTracksQuery(playlistId, paging.Number, Size(paging.PageSize ?? DefaultPageSize)),
            cancellationToken);

        if (tracks is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(tracks.Version);

        return Results.Ok(tracks.Tracks);
    }

    private static async Task<IResult> AddAsync(
        Guid playlistId,
        TrackAddition request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        AddTrackCommand command = new(
            playlistId,
            request.YoutubeVideoId,
            request.AfterTrackId,
            request.AtHead ?? false,
            expectedVersion);

        try
        {
            TrackWriteResult result = await sender.Send(command, cancellationToken);

            return result.Outcome == TrackWriteOutcome.Written
                ? Results.Created(
                    $"/api/v1/playlists/{playlistId}/tracks/{result.PlaylistTrackId}",
                    new { playlistTrackId = result.PlaylistTrackId })
                : Refusal(result.Outcome);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> MoveAsync(
        [AsParameters] TrackPath path,
        TrackDestination request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        MoveTrackCommand command = new(
            path.PlaylistId,
            path.PlaylistTrackId,
            request.AfterTrackId,
            request.BeforeTrackId,
            expectedVersion);

        try
        {
            TrackWriteResult result = await sender.Send(command, cancellationToken);

            return result.Outcome == TrackWriteOutcome.Written ? Results.Ok() : Refusal(result.Outcome);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> RemoveAsync(
        Guid playlistId,
        Guid playlistTrackId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        TrackWriteResult result = await sender.Send(
            new RemoveTrackCommand(playlistId, playlistTrackId, expectedVersion),
            cancellationToken);

        return result.Outcome == TrackWriteOutcome.Written ? Results.NoContent() : Refusal(result.Outcome);
    }

    /// <summary>
    /// The one status a refused track write answers with. A playlist the caller may not edit and a playlist
    /// that does not exist are the same `404` — SPEC-014's rule, unchanged here.
    /// </summary>
    private static IResult Refusal(TrackWriteOutcome outcome) => outcome switch
    {
        TrackWriteOutcome.VersionMismatch => Results.StatusCode(StatusCodes.Status412PreconditionFailed),
        TrackWriteOutcome.PlaylistFull => Results.Conflict(),
        _ => Results.NotFound(),
    };

    private static int Size(int pageSize) =>
        Math.Min(Math.Max(pageSize, 1), PlaylistEndpoints.MaximumPageSize);
}

/// <summary>The add-a-track payload — <c>docs/api/ENDPOINTS.md</c> §5.</summary>
/// <param name="YoutubeVideoId">The video.</param>
/// <param name="AfterTrackId">Put it after this track. Absent means the tail.</param>
/// <param name="AtHead">True to put it before everything.</param>
internal sealed record TrackAddition(string YoutubeVideoId, Guid? AfterTrackId, bool? AtHead);

/// <summary>The two route values that name one track.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="PlaylistTrackId">The track.</param>
/// <remarks>
/// Bound as one parameter so the handler stays inside the five-parameter gate. The route template still
/// declares both values, so the contract and the generated document are unchanged.
/// </remarks>
internal sealed record TrackPath(Guid PlaylistId, Guid PlaylistTrackId);

/// <summary>The paging query of the track list.</summary>
/// <param name="Page">The page, from 1. Absent means the first.</param>
/// <param name="PageSize">How many rows, capped by the endpoint.</param>
/// <remarks>
/// The query names are spelled out so the generated document advertises <c>page</c> and <c>pageSize</c>, the
/// same casing every other paged route in §5 uses. Without them the record's property names would leak into
/// the contract.
/// </remarks>
internal sealed record TrackPaging(
    [FromQuery(Name = "page")] int? Page,
    [FromQuery(Name = "pageSize")] int? PageSize)
{
    /// <summary>The page to read, never below 1.</summary>
    public int Number => Page is int asked && asked > 1 ? asked : 1;
}

/// <summary>The move payload. Exactly one neighbour, or `422`.</summary>
/// <param name="AfterTrackId">Put it after this track.</param>
/// <param name="BeforeTrackId">Put it before this track.</param>
internal sealed record TrackDestination(Guid? AfterTrackId, Guid? BeforeTrackId);
