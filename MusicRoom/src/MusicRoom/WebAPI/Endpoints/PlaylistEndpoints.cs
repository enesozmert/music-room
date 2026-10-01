using System.Globalization;
using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playlists.Commands.CreatePlaylist;
using MusicRoom.Application.Features.Playlists.Commands.DeletePlaylist;
using MusicRoom.Application.Features.Playlists.Commands.RenamePlaylist;
using MusicRoom.Application.Features.Playlists.Commands.SetPlaylistLicence;
using MusicRoom.Application.Features.Playlists.Queries.GetPlaylist;
using MusicRoom.Application.Features.Playlists.Queries.ListPlaylists;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Playlists under <c>/api/v1/playlists</c> — <c>docs/api/ENDPOINTS.md</c> §5.
/// </summary>
/// <remarks>
/// The `ETag` is PostgreSQL's <c>xmin</c> for the row, and `If-Match` on the metadata `PATCH` is compared
/// against it inside the `UPDATE` — so a stale client changes nothing and is told `412`. A playlist the caller
/// may not see answers `404`, never `403`.
/// </remarks>
public static class PlaylistEndpoints
{
    /// <summary>The largest page a caller may ask for — an unbounded list is OWASP API4.</summary>
    public const int MaximumPageSize = 50;

    /// <summary>Maps the playlist routes and, through its own class, the collaborator routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapPlaylistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/playlists");

        MapCreate(group);
        MapList(group);
        MapGet(group);
        MapRename(group);
        MapSetLicence(group);
        MapDelete(group);

        PlaylistCollaboratorEndpoints.Map(group);

        return endpoints;
    }

    private static void MapCreate(RouteGroupBuilder group) =>
        group.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .WithName("PostPlaylist")
            .WithSummary("Create a playlist")
            .WithDescription(
                "The caller owns it. A playlist may be Everyone or InvitedOnly; LocationAndTimeRestricted is "
                + "422, because that licence belongs to an event and nothing checks a geofence on a playlist.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("GetPlaylists")
            .WithSummary("List playlists")
            .WithDescription(
                "One scope at a time: mine, shared with me, or public. Paged with a capped size. A private "
                + "playlist the caller is not named on never appears in any scope.")
            .Produces<IReadOnlyList<PlaylistSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapGet(RouteGroupBuilder group) =>
        group.MapGet("/{playlistId:guid}", GetAsync)
            .RequireAuthorization()
            .WithName("GetPlaylist")
            .WithSummary("Read one playlist")
            .WithDescription(
                "Returns the playlist with an ETag the client sends back as If-Match when it edits. A playlist "
                + "the caller may not see answers 404, never 403, because 403 would confirm it exists.")
            .Produces<PlaylistResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapRename(RouteGroupBuilder group) =>
        group.MapPatch("/{playlistId:guid}", RenameAsync)
            .RequireAuthorization()
            .WithName("PatchPlaylist")
            .WithSummary("Change a playlist's metadata")
            .WithDescription(
                "Requires If-Match carrying the ETag the client last read. A stale or absent token answers 412 "
                + "and changes nothing: two clients editing at once is the failure this guard exists for.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapSetLicence(RouteGroupBuilder group) =>
        group.MapPut("/{playlistId:guid}/licence", SetLicenceAsync)
            .RequireAuthorization()
            .WithName("PutPlaylistLicence")
            .WithSummary("Set a playlist's visibility and licence")
            .WithDescription(
                "Owner only: a collaborator who may edit the contents does not decide who may see them.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapDelete(RouteGroupBuilder group) =>
        group.MapDelete("/{playlistId:guid}", DeleteAsync)
            .RequireAuthorization()
            .WithName("DeletePlaylist")
            .WithSummary("Delete a playlist")
            .WithDescription(
                "A soft delete, owner only: the row is stamped deleted and disappears from every read. Its "
                + "audit trail keeps pointing at something that exists.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> CreateAsync(
        PlaylistCreation request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        CreatePlaylistCommand command = new(
            request.Name,
            request.Visibility ?? Visibility.Private,
            request.Licence ?? LicenceMode.InvitedOnly);

        try
        {
            Guid playlistId = await sender.Send(command, cancellationToken);

            return Results.Created($"/api/v1/playlists/{playlistId}", new { playlistId });
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        string scope = "mine",
        int page = 1,
        int pageSize = 20)
    {
        if (!Enum.TryParse(scope, ignoreCase: true, out PlaylistScope parsed))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["scope"] = ["scope must be 'mine', 'shared' or 'public'."],
                },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        IReadOnlyList<PlaylistSummary> playlists = await sender.Send(
            new ListPlaylistsQuery(parsed, page < 1 ? 1 : page, Size(pageSize)),
            cancellationToken);

        return Results.Ok(playlists);
    }

    private static async Task<IResult> GetAsync(
        Guid playlistId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        PlaylistDetail? playlist = await sender.Send(new GetPlaylistQuery(playlistId), cancellationToken);

        if (playlist is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ETag(playlist.Version);

        return Results.Ok(new PlaylistResponse(
            playlist.PlaylistId,
            playlist.Name,
            playlist.Visibility,
            playlist.Licence,
            playlist.OwnerId,
            playlist.MayEdit));
    }

    private static async Task<IResult> RenameAsync(
        Guid playlistId,
        PlaylistMetadata request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // An absent If-Match is treated as a mismatch rather than allowed through: the contract lists 412 and
        // not 428, and "you forgot the precondition" and "your precondition failed" have the same fix — reload.
        if (!TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        try
        {
            PlaylistWriteOutcome outcome = await sender.Send(
                new RenamePlaylistCommand(playlistId, request.Name, expectedVersion),
                cancellationToken);

            return outcome switch
            {
                PlaylistWriteOutcome.Updated => Results.Ok(),
                PlaylistWriteOutcome.VersionMismatch =>
                    Results.StatusCode(StatusCodes.Status412PreconditionFailed),
                _ => Results.NotFound(),
            };
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> SetLicenceAsync(
        Guid playlistId,
        PlaylistLicenceRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            bool updated = await sender.Send(
                new SetPlaylistLicenceCommand(playlistId, request.Visibility, request.Licence),
                cancellationToken);

            return updated ? Results.Ok() : Results.NotFound();
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid playlistId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new DeletePlaylistCommand(playlistId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    /// <summary>The `xmin` of a row, as a weak-free quoted `ETag`.</summary>
    internal static string ETag(uint version) =>
        $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// Reads `If-Match` back into an `xmin`. A missing, malformed or wildcard header fails: a wildcard means
    /// "whatever is there", which is exactly the last-write-wins behaviour this guard exists to prevent.
    /// </summary>
    private static bool TryReadIfMatch(HttpContext httpContext, out uint expectedVersion)
    {
        expectedVersion = 0;

        string? header = httpContext.Request.Headers.IfMatch.ToString();

        return !string.IsNullOrWhiteSpace(header)
            && uint.TryParse(
                header.Trim().Trim('"'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out expectedVersion);
    }

    private static int Size(int pageSize) => Math.Min(Math.Max(pageSize, 1), MaximumPageSize);
}

/// <summary>The playlist creation payload — <c>docs/api/ENDPOINTS.md</c> §5.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Visibility">Who may find it. Defaults to private.</param>
/// <param name="Licence">Who may write to it. Defaults to invited only.</param>
internal sealed record PlaylistCreation(string Name, Visibility? Visibility, LicenceMode? Licence);

/// <summary>The metadata payload for the guarded `PATCH`.</summary>
/// <param name="Name">The new name.</param>
internal sealed record PlaylistMetadata(string Name);

/// <summary>The licence payload.</summary>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it.</param>
internal sealed record PlaylistLicenceRequest(Visibility Visibility, LicenceMode Licence);

/// <summary>One playlist, as a client reads it.</summary>
/// <param name="PlaylistId">The playlist.</param>
/// <param name="Name">Its name.</param>
/// <param name="Visibility">Who may find it.</param>
/// <param name="Licence">Who may write to it.</param>
/// <param name="OwnerId">Its owner.</param>
/// <param name="MayEdit">Whether this caller may write to it, so a client can hide an editor it cannot use.</param>
internal sealed record PlaylistResponse(
    Guid PlaylistId,
    string Name,
    Visibility Visibility,
    LicenceMode Licence,
    Guid OwnerId,
    bool MayEdit);
