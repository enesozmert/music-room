using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Devices.Commands.GrantControl;
using MusicRoom.Application.Features.Devices.Commands.ReleaseControl;
using MusicRoom.Application.Features.Devices.Queries.GetDeviceControl;
using MusicRoom.Application.Features.Devices.Queries.ListControlGrants;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Control delegation — <c>docs/api/ENDPOINTS.md</c> §6, subject V.2.2.
/// </summary>
/// <remarks>
/// The `ETag` is the device row's <c>xmin</c>, and `If-Match` is compared **inside** the `UPDATE`, so two
/// simultaneous claims resolve to one `200` and one `412`. The loser must lose visibly: two clients each
/// believing they hold one device is the failure this endpoint exists to prevent.
/// <para>
/// A grantee must be a friend of the owner, checked server-side against the graph SPEC-012 built, and refused
/// with `422` naming the rule. Its own class so <see cref="DeviceEndpoints"/> stays inside the file-length and
/// coupling gates.
/// </para>
/// </remarks>
internal static class DeviceControlEndpoints
{
    /// <summary>Maps the three control routes onto the device group.</summary>
    /// <param name="group">The <c>/api/v1/devices</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapRead(group);
        MapGrant(group);
        MapRelease(group);
    }

    /// <summary>Maps the caller's own grant list, which lives under <c>/api/v1/me</c>.</summary>
    /// <param name="endpoints">The route builder.</param>
    internal static void MapGrantList(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/me/control-grants", GrantsAsync)
            .RequireAuthorization()
            .WithName("GetControlGrants")
            .WithSummary("List the devices the caller has been given control of")
            .WithDescription(
                "The delegation seen from the other side: what somebody else handed me, not what I handed out.")
            .Produces<IReadOnlyList<ControlGrantSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapRead(RouteGroupBuilder group) =>
        group.MapGet("/{deviceId:guid}/control", ReadAsync)
            .RequireAuthorization()
            .WithName("GetDeviceControl")
            .WithSummary("Read who controls a device")
            .WithDescription(
                "The owner or the current controller may ask; anybody else gets 404. With no grant the "
                + "controller is the owner. The response carries the ETag a claim sends back as If-Match.")
            .Produces<DeviceControl>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapGrant(RouteGroupBuilder group) =>
        group.MapPut("/{deviceId:guid}/control", GrantAsync)
            .RequireAuthorization()
            .WithName("PutDeviceControl")
            .WithSummary("Hand playback control to a friend")
            .WithDescription(
                "Owner only, and requires If-Match. The grantee must be a friend of the owner, or the answer is "
                + "422. Two simultaneous claims: one 200, one 412 — the loser loses visibly, because two "
                + "clients each believing they hold one device is the bug.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapRelease(RouteGroupBuilder group) =>
        group.MapDelete("/{deviceId:guid}/control", ReleaseAsync)
            .RequireAuthorization()
            .WithName("DeleteDeviceControl")
            .WithSummary("Return a device to its owner's control")
            .WithDescription(
                "The owner's revoke and the controller's release are the same route and the same effect. A "
                + "device already under owner control answers 404: there was no grant to release.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> ReadAsync(
        Guid deviceId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DeviceControl? control = await sender.Send(
            new GetDeviceControlQuery(deviceId),
            cancellationToken);

        if (control is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(control.Version);

        return Results.Ok(control);
    }

    private static async Task<IResult> GrantAsync(
        Guid deviceId,
        ControlGrantRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // An absent If-Match is a mismatch, as on every other guarded write here: the contract lists 412 and
        // not 428, and "you forgot the precondition" and "your precondition failed" have the same fix — reload.
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        try
        {
            DeviceWriteOutcome outcome = await sender.Send(
                new GrantControlCommand(deviceId, request.GranteeUserId, expectedVersion),
                cancellationToken);

            return outcome switch
            {
                DeviceWriteOutcome.Written => Results.Ok(),
                DeviceWriteOutcome.VersionMismatch =>
                    Results.StatusCode(StatusCodes.Status412PreconditionFailed),
                DeviceWriteOutcome.NotAFriend => Results.ValidationProblem(
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["granteeUserId"] = ["Control may only be delegated to a friend of the owner."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity),
                _ => Results.NotFound(),
            };
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }
    }

    private static async Task<IResult> ReleaseAsync(
        Guid deviceId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new ReleaseControlCommand(deviceId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> GrantsAsync(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new ListControlGrantsQuery(), cancellationToken));
}

/// <summary>The control grant payload — <c>docs/api/ENDPOINTS.md</c> §6.</summary>
/// <param name="GranteeUserId">Who is being given control. Must be a friend of the owner.</param>
internal sealed record ControlGrantRequest(Guid GranteeUserId);
