using FluentValidation;
using MediatR;
using MusicRoom.Application.Features.Events.Commands.SetEventLicence;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// `PUT /api/v1/events/{eventId}/licence` — <c>docs/api/ENDPOINTS.md</c> §4, ADR-0014.
/// </summary>
/// <remarks>
/// Its own class: who may find an event and who may vote in it is a policy decision, not metadata, and keeping
/// it apart is also what keeps <see cref="EventEndpoints"/> inside the coupling gate.
/// <para>
/// Owner only, and refused with `422` on exactly the rules creation applies — a `LocationAndTimeRestricted`
/// event must say which proof it requires, and a geofence proof must carry a centre and a radius. An event must
/// not be able to *become* a restricted event with nothing configured to check.
/// </para>
/// </remarks>
internal static class EventLicenceEndpoints
{
    /// <summary>Maps the route onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group) =>
        group.MapPut("/{eventId:guid}/licence", SetAsync)
            .RequireAuthorization()
            .WithName("PutEventLicence")
            .WithSummary("Set an event's visibility, licence and presence policy")
            .WithDescription(
                "Owner only. The same rules creation applies: an event cannot become a "
                + "LocationAndTimeRestricted event with nothing configured to check.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> SetAsync(
        Guid eventId,
        EventLicenceRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            bool updated = await sender.Send(
                new SetEventLicenceCommand(eventId, request.ToLicence()),
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
}
