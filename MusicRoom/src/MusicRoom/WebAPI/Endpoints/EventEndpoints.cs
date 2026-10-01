using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events.Commands.CreateEvent;
using MusicRoom.Application.Features.Events.Commands.DeleteEvent;
using MusicRoom.Application.Features.Events.Commands.UpdateEvent;
using MusicRoom.Application.Features.Events.Queries.GetEvent;
using MusicRoom.Application.Features.Events.Queries.ListEvents;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Events under <c>/api/v1/events</c> — <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <remarks>
/// The aggregate's five routes plus the licence. The mechanism is the playlist aggregate's, reused rather than
/// reinvented: the `ETag` is PostgreSQL's <c>xmin</c>, `If-Match` is compared inside the `UPDATE`, and an event
/// the caller may not see answers `404` and never `403`.
/// <para>
/// A `LocationAndTimeRestricted` event must say which presence proof it requires, and a geofence proof must
/// carry a centre and a radius — `422` otherwise. A licence that claims to restrict by place and time with
/// nothing configured to check would read as a feature and behave as an open door (ADR-0014, SPEC-017).
/// </para>
/// </remarks>
public static class EventEndpoints
{
    /// <summary>The largest page a caller may ask for — an unbounded list is OWASP API4.</summary>
    public const int MaximumPageSize = 50;

    /// <summary>Maps the event routes, and through one child class the licence and roster routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/events");

        MapCreate(group);
        MapList(group);
        MapGet(group);
        MapUpdate(group);
        MapDelete(group);

        EventChildRoutes.Map(group);

        return endpoints;
    }

    private static void MapCreate(RouteGroupBuilder group) =>
        group.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .WithName("PostEvent")
            .WithSummary("Create an event")
            .WithDescription(
                "The caller owns it. The window is two UTC instants and the end must be after the start. A "
                + "LocationAndTimeRestricted licence must name the presence proof it requires, and a geofence "
                + "proof must carry a centre and a radius, or the answer is 422.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapList(RouteGroupBuilder group) =>
        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("GetEvents")
            .WithSummary("List events")
            .WithDescription(
                "One scope at a time: mine, the ones I was invited to, or public. Paged with a capped size. A "
                + "private event the caller was not invited to appears in none of them.")
            .Produces<IReadOnlyList<EventSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapGet(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}", GetAsync)
            .RequireAuthorization()
            .WithName("GetEvent")
            .WithSummary("Read one event")
            .WithDescription(
                "Returns the event with an ETag the client sends back as If-Match when it edits. An event the "
                + "caller may not see answers 404, never 403, because 403 would confirm it exists.")
            .Produces<EventResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapUpdate(RouteGroupBuilder group) =>
        group.MapPatch("/{eventId:guid}", UpdateAsync)
            .RequireAuthorization()
            .WithName("PatchEvent")
            .WithSummary("Change an event's metadata")
            .WithDescription(
                "Owner only, and requires If-Match carrying the ETag the client last read. A stale or absent "
                + "token answers 412 and changes nothing.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapDelete(RouteGroupBuilder group) =>
        group.MapDelete("/{eventId:guid}", DeleteAsync)
            .RequireAuthorization()
            .WithName("DeleteEvent")
            .WithSummary("Delete an event")
            .WithDescription(
                "A soft delete, owner only: the row is stamped deleted and disappears from every read, so its "
                + "audit trail keeps pointing at something that exists.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> CreateAsync(
        EventCreation request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            Guid eventId = await sender.Send(
                new CreateEventCommand(request.ToMetadata(), request.ToLicence()),
                cancellationToken);

            return Results.Created($"/api/v1/events/{eventId}", new { eventId });
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
        if (!Enum.TryParse(scope, ignoreCase: true, out EventScope parsed))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["scope"] = ["scope must be 'mine', 'invited' or 'public'."],
                },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        IReadOnlyList<EventSummary> events = await sender.Send(
            new ListEventsQuery(parsed, page < 1 ? 1 : page, Size(pageSize)),
            cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> GetAsync(
        Guid eventId,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        EventDetail? found = await sender.Send(new GetEventQuery(eventId), cancellationToken);

        if (found is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.ETag = ConcurrencyHeaders.ETag(found.Version);

        return Results.Ok(EventResponse.From(found));
    }

    private static async Task<IResult> UpdateAsync(
        Guid eventId,
        EventMetadataRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // An absent If-Match is treated as a mismatch, as on a playlist: the contract lists 412 and not 428,
        // and "you forgot the precondition" and "your precondition failed" have the same fix — reload.
        if (!ConcurrencyHeaders.TryReadIfMatch(httpContext, out uint expectedVersion))
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        try
        {
            EventWriteOutcome outcome = await sender.Send(
                new UpdateEventCommand(
                    eventId,
                    new EventMetadata(request.Name, request.StartsAtUtc, request.EndsAtUtc),
                    expectedVersion),
                cancellationToken);

            return outcome switch
            {
                EventWriteOutcome.Updated => Results.Ok(),
                EventWriteOutcome.VersionMismatch =>
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

    private static async Task<IResult> DeleteAsync(
        Guid eventId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new DeleteEventCommand(eventId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static int Size(int pageSize) => Math.Min(Math.Max(pageSize, 1), MaximumPageSize);
}
