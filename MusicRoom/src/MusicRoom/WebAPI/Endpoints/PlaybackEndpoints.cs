using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playback.Commands.AdvanceSession;
using MusicRoom.Application.Features.Playback.Commands.JumpToItem;
using MusicRoom.Application.Features.Playback.Commands.StartSession;
using MusicRoom.Application.Features.Playback.Commands.StopSession;
using MusicRoom.Application.Features.Playback.Queries.GetSession;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Playback sessions under <c>/api/v1/playback-sessions</c> — <c>docs/api/ENDPOINTS.md</c> §7, ADR-0017.
/// </summary>
/// <remarks>
/// **The server decides what plays; the client decides when.** An advance says "the track ended", and the next
/// item comes from the source's own order — the highest vote count for an event, the next position for a playlist.
/// <para>
/// Two kinds of staleness get two answers. A stale `If-Match` is `412`: somebody else moved the session first. A
/// stale `expectedNowPlayingId` is `200` with the current state: this is a retry of a move that already happened,
/// and a retry must not skip a second track (SPEC-021 decision 4).
/// </para>
/// </remarks>
public static class PlaybackEndpoints
{
    /// <summary>Maps the playback routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/playback-sessions");

        MapStart(group);
        MapGet(group);
        MapAdvance(group);
        MapJump(group);
        MapStop(group);

        return endpoints;
    }

    private static void MapStart(RouteGroupBuilder group) =>
        group.MapPost("/", StartAsync)
            .RequireAuthorization()
            .WithName("PostPlaybackSession")
            .WithSummary("Start playing a source on a device")
            .WithDescription(
                "The device's owner starts playback, on a source they may see. The server picks what plays "
                + "first. A device already playing answers 409: it holds at most one live session.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

    private static void MapGet(RouteGroupBuilder group) =>
        group.MapGet("/{sessionId:guid}", GetAsync)
            .RequireAuthorization()
            .WithName("GetPlaybackSession")
            .WithSummary("Read one playback session")
            .WithDescription(
                "Anybody who may see the source may see what it is playing, with the ETag a command sends back "
                + "as If-Match. A caller who may not see the source answers 404.")
            .Produces<PlaybackSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapAdvance(RouteGroupBuilder group) =>
        group.MapPost("/{sessionId:guid}/advances", AdvanceAsync)
            .RequireAuthorization()
            .WithName("PostPlaybackAdvance")
            .WithSummary("Report that the track ended")
            .WithDescription(
                "The controller's route. The server picks the next item — highest vote count for an event, next "
                + "position for a playlist. `expectedNowPlayingId` makes a retry harmless: a stale expectation "
                + "returns the current state rather than skipping a second track. A stale If-Match is 412. "
                + "Nothing left to play ends the session and answers 409.")
            .Produces<PlaybackSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status412PreconditionFailed);

    private static void MapJump(RouteGroupBuilder group) =>
        group.MapPatch("/{sessionId:guid}", JumpAsync)
            .RequireAuthorization()
            .WithName("PatchPlaybackSession")
            .WithSummary("Jump to a named item")
            .WithDescription(
                "The one route where the client chooses what plays, so it is guarded and bounded: the controller "
                + "may jump, and only to an item of the source already playing.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed);

    private static void MapStop(RouteGroupBuilder group) =>
        group.MapDelete("/{sessionId:guid}", StopAsync)
            .RequireAuthorization()
            .WithName("DeletePlaybackSession")
            .WithSummary("Stop playback")
            .WithDescription(
                "The device's owner stops it: a delegate steers what plays and does not get to switch the "
                + "speaker off. The row is stamped ended rather than deleted, and the device may start again.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> StartAsync(
        PlaybackStartRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        SessionStart start = await sender.Send(
            new StartSessionCommand(
                new PlaybackSource(request.DeviceId, request.SourceType, request.SourceId)),
            cancellationToken);

        return start.Outcome switch
        {
            SessionStartOutcome.Started => Results.Created(
                $"/api/v1/playback-sessions/{start.SessionId}",
                new { sessionId = start.SessionId, nowPlayingId = start.NowPlayingId }),
            SessionStartOutcome.AlreadyPlaying => Results.Conflict(),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> GetAsync(
        Guid sessionId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await sender.Send(
            new GetSessionQuery(sessionId),
            cancellationToken);

        if (session is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(session.Version);

        return Results.Ok(session);
    }

    private static async Task<IResult> AdvanceAsync(
        Guid sessionId,
        PlaybackAdvanceRequest? request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        PlaybackWriteResult result = await sender.Send(
            new AdvanceSessionCommand(sessionId, request?.ExpectedNowPlayingId, expectedVersion),
            cancellationToken);

        return result.Outcome switch
        {
            // Advanced and "already moved" are both success, and both answer with where the session is. The
            // second is a retry that correctly did nothing — ADR-0017.
            PlaybackWriteOutcome.Advanced or PlaybackWriteOutcome.AlreadyMoved =>
                Results.Ok(new { sessionId, nowPlayingId = result.NowPlayingId }),
            PlaybackWriteOutcome.QueueExhausted => Results.Conflict(),
            PlaybackWriteOutcome.VersionMismatch =>
                Results.StatusCode(StatusCodes.Status412PreconditionFailed),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> JumpAsync(
        Guid sessionId,
        PlaybackJumpRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        PlaybackWriteResult result = await sender.Send(
            new JumpToItemCommand(sessionId, request.NowPlayingId, expectedVersion),
            cancellationToken);

        return result.Outcome switch
        {
            PlaybackWriteOutcome.Advanced => Results.Ok(
                new { sessionId, nowPlayingId = result.NowPlayingId }),
            PlaybackWriteOutcome.VersionMismatch =>
                Results.StatusCode(StatusCodes.Status412PreconditionFailed),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> StopAsync(
        Guid sessionId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new StopSessionCommand(sessionId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
}

/// <summary>The session-start payload — <c>docs/api/ENDPOINTS.md</c> §7.</summary>
/// <param name="DeviceId">The device, which the caller must own.</param>
/// <param name="SourceType">Whether the source is an event or a playlist.</param>
/// <param name="SourceId">The event or the playlist.</param>
internal sealed record PlaybackStartRequest(
    Guid DeviceId,
    PlaybackSourceType SourceType,
    Guid SourceId);

/// <summary>The advance payload.</summary>
/// <param name="ExpectedNowPlayingId">
/// What the client believes is playing. A mismatch means this is a retry of a move that already happened.
/// </param>
internal sealed record PlaybackAdvanceRequest(Guid? ExpectedNowPlayingId);

/// <summary>The jump payload.</summary>
/// <param name="NowPlayingId">The item to play, which must belong to the session's source.</param>
internal sealed record PlaybackJumpRequest(Guid NowPlayingId);
