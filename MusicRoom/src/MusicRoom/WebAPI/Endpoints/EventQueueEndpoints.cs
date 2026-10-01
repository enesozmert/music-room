using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events.Commands.RemoveSuggestion;
using MusicRoom.Application.Features.Events.Commands.SuggestTrack;
using MusicRoom.Application.Features.Events.Queries.GetQueue;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Suggestions and the vote-ordered queue — <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <remarks>
/// The queue's order is **counted from the vote rows on every read**. No column holds it, so no two writers can
/// disagree about it and no increment can be lost — SPEC-018 decision 2, SDD §12.2.
/// </remarks>
internal static class EventQueueEndpoints
{
    /// <summary>Maps the three routes onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapQueue(group);
        MapSuggest(group);
        MapRemove(group);
    }

    private static void MapQueue(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}/queue", QueueAsync)
            .RequireAuthorization()
            .WithName("GetEventQueue")
            .WithSummary("Read the vote-ordered queue")
            .WithDescription(
                "Anybody who may see the event may see its queue. Ordered by vote count and then by when each "
                + "track was suggested, so the order is total and stable. Each row says whether the caller "
                + "voted, and the count is read from the vote rows rather than from a stored counter.")
            .Produces<IReadOnlyList<QueueEntry>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapSuggest(RouteGroupBuilder group) =>
        group.MapPost("/{eventId:guid}/tracks", SuggestAsync)
            .RequireAuthorization()
            .WithName("PostEventTrack")
            .WithSummary("Suggest a track for an event")
            .WithDescription(
                "Behind the licence gate: who may suggest is who may vote. The same video twice is 409, because "
                + "two rows would split its votes between two queue entries.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapRemove(RouteGroupBuilder group) =>
        group.MapDelete("/{eventId:guid}/tracks/{suggestionId:guid}", RemoveAsync)
            .RequireAuthorization()
            .WithName("DeleteEventTrack")
            .WithSummary("Remove a suggestion")
            .WithDescription(
                "The suggester's or the event owner's, and nobody else's. Its votes go with it: a vote is a vote "
                + "for that suggestion, and orphaned rows would leave a count that counts nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> QueueAsync(
        Guid eventId,
        ISender sender,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        IReadOnlyList<QueueEntry>? queue = await sender.Send(
            new GetQueueQuery(eventId, page < 1 ? 1 : page, Size(pageSize)),
            cancellationToken);

        return queue is null ? Results.NotFound() : Results.Ok(queue);
    }

    private static async Task<IResult> SuggestAsync(
        Guid eventId,
        SuggestionRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            SuggestionWriteResult result = await sender.Send(
                new SuggestTrackCommand(eventId, request.YoutubeVideoId),
                cancellationToken);

            return result.Outcome switch
            {
                SuggestionWriteOutcome.Added => Results.Created(
                    $"/api/v1/events/{eventId}/tracks/{result.SuggestionId}",
                    new { suggestionId = result.SuggestionId }),
                SuggestionWriteOutcome.Duplicate => Results.Conflict(),
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

    private static async Task<IResult> RemoveAsync(
        Guid eventId,
        Guid suggestionId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new RemoveSuggestionCommand(eventId, suggestionId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static int Size(int pageSize) =>
        Math.Min(Math.Max(pageSize, 1), EventEndpoints.MaximumPageSize);
}

/// <summary>The suggestion payload — <c>docs/api/ENDPOINTS.md</c> §4.</summary>
/// <param name="YoutubeVideoId">The video.</param>
internal sealed record SuggestionRequest(string YoutubeVideoId);
