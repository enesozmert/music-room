using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playback.Queries.GetEventSession;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// What an event is playing — <c>docs/api/ENDPOINTS.md</c> §7, SPEC-058.
/// </summary>
/// <remarks>
/// Its own class for the reason <see cref="DevicePlaybackEndpoints"/> is: the route hangs off an event and
/// the answer is a playback session, so putting it in either group would push that group past the coupling
/// gate.
/// </remarks>
internal static class EventPlaybackEndpoints
{
    /// <summary>Maps the route onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}/playback-session", GetAsync)
            .RequireAuthorization()
            .WithName("GetEventPlaybackSession")
            .WithSummary("Read what an event is playing")
            .WithDescription(
                "Anybody who may see the event may see what it is playing, which is the rule "
                + "GET /playback-sessions/{sessionId} already applies — this route is for the guest who "
                + "has the event and not the session id. The answer carries nowPlayingStartedAtUtc, so a "
                + "guest arriving mid-track starts from the right second rather than from the beginning, "
                + "and serverNowUtc, so they can correct for their own clock. An event with nothing "
                + "playing, and an event the caller may not see, both answer 404. When two devices are "
                + "playing one event, the newest live session answers.")
            .Produces<PlaybackSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> GetAsync(
        Guid eventId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await sender.Send(
            new GetEventSessionQuery(eventId),
            cancellationToken);

        if (session is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(session.Version);

        return Results.Ok(session);
    }
}
