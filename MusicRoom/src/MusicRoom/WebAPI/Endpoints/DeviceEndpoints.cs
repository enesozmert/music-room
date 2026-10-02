using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Devices.Commands.RegisterDevice;
using MusicRoom.Application.Features.Devices.Commands.RemoveDevice;
using MusicRoom.Application.Features.Devices.Queries.ListDevices;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Devices under <c>/api/v1/devices</c> — <c>docs/api/ENDPOINTS.md</c> §6.
/// </summary>
/// <remarks>
/// Registration carries **no body**: the platform, the model and the application version come from the mandatory
/// client headers the request already had to send (SDD §10.7). A body repeating them would be a second source
/// that can disagree with the audit record written for the same request — SPEC-020 decision 7.
/// </remarks>
public static class DeviceEndpoints
{
    /// <summary>Maps the device routes and, through its own class, the control routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/devices");

        MapRegister(group);
        MapList(group);
        MapRemove(group);

        DeviceControlEndpoints.Map(group);
        DevicePlaybackEndpoints.Map(group);
        DeviceControlEndpoints.MapGrantList(endpoints);

        return endpoints;
    }

    private static void MapRegister(RouteGroupBuilder group) =>
        group.MapPost("/", RegisterAsync)
            .RequireAuthorization()
            .WithName("PostDevice")
            .WithSummary("Register the calling device")
            .WithDescription(
                "Takes no body: the platform, the model and the app version come from the mandatory client "
                + "headers. Registering the same device twice is 409, where the same device means the same "
                + "owner, platform and model — a phone that updates its app is the same phone.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("GetDevices")
            .WithSummary("List the caller's own devices")
            .WithDescription(
                "Only the caller's. The route takes no account id, because a route that accepted one would be a "
                + "route somebody would eventually pass somebody else's.")
            .Produces<IReadOnlyList<DeviceSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapRemove(RouteGroupBuilder group) =>
        group.MapDelete("/{deviceId:guid}", RemoveAsync)
            .RequireAuthorization()
            .WithName("DeleteDevice")
            .WithSummary("Remove one of the caller's devices")
            .WithDescription(
                "Owner only. Another account's device answers 404, never 403, because 403 would confirm it "
                + "exists.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> RegisterAsync(ISender sender, CancellationToken cancellationToken)
    {
        DeviceRegistration registration = await sender.Send(
            new RegisterDeviceCommand(),
            cancellationToken);

        return registration.Outcome switch
        {
            DeviceWriteOutcome.Written => Results.Created(
                $"/api/v1/devices/{registration.DeviceId}",
                new { deviceId = registration.DeviceId }),
            _ => Results.Conflict(),
        };
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new ListDevicesQuery(), cancellationToken));

    private static async Task<IResult> RemoveAsync(
        Guid deviceId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new RemoveDeviceCommand(deviceId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
}
