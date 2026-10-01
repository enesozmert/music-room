using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Playback.Queries.GetDeviceSession;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// What a device is playing — <c>docs/api/ENDPOINTS.md</c> §7's third row, on the device group.
/// </summary>
/// <remarks>
/// Its own class because it belongs to two things at once: the route hangs off a device, and the answer is a
/// playback session. Keeping it apart leaves both <see cref="DeviceEndpoints"/> and
/// <see cref="PlaybackEndpoints"/> inside the coupling gate.
/// </remarks>
internal static class DevicePlaybackEndpoints
{
    /// <summary>Maps the route onto the device group.</summary>
    /// <param name="group">The <c>/api/v1/devices</c> route group.</param>
    internal static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{deviceId:guid}/playback-session", GetAsync)
            .RequireAuthorization()
            .WithName("GetDevicePlaybackSession")
            .WithSummary("Read what a device is playing")
            .WithDescription(
                "The device's owner or its current controller: the people who could change what it plays are the "
                + "people entitled to know what it is playing. A device that is not playing answers 404.")
            .Produces<PlaybackSessionDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> GetAsync(
        Guid deviceId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        PlaybackSessionDetail? session = await sender.Send(
            new GetDeviceSessionQuery(deviceId),
            cancellationToken);

        if (session is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(session.Version);

        return Results.Ok(session);
    }
}
