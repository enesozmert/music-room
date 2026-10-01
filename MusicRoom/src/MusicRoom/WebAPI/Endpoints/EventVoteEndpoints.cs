using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events.Commands.CastVote;
using MusicRoom.Application.Features.Events.Commands.IssueEventCode;
using MusicRoom.Application.Features.Events.Commands.WithdrawVote;
using MusicRoom.Application.Features.Events.Presence;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The vote, its withdrawal, and the venue codes — <c>docs/api/ENDPOINTS.md</c> §4, ADR-0014.
/// </summary>
/// <remarks>
/// A vote is `PUT` and idempotent: `201` the first time, `200` after, and `200` again when a concurrent request
/// wrote the row first. A double submit from a flaky mobile network is therefore harmless, which is the reason
/// the contract chose `PUT` over `POST`.
/// <para>
/// On a `LocationAndTimeRestricted` event the request carries `presence`, and a missing or failed proof is
/// `422`. The coordinate is compared against the event's geofence and dropped — never stored on the vote, never
/// returned, never logged (SPEC-018 decision 5).
/// </para>
/// </remarks>
internal static class EventVoteEndpoints
{
    /// <summary>
    /// The life a code gets when the request names none — fifteen minutes.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because the .editorconfig naming rule requires an underscore prefix on a
    /// private field, and a const is a field to that rule.
    /// </remarks>
    internal const int DefaultTimeToLiveSeconds = 900;

    /// <summary>Maps the three routes onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapVote(group);
        MapWithdraw(group);
        MapIssueCode(group);
    }

    private static void MapVote(RouteGroupBuilder group) =>
        group.MapPut("/{eventId:guid}/tracks/{suggestionId:guid}/vote", VoteAsync)
            .RequireAuthorization()
            .RequireRateLimiting(EventRateLimiterPolicies.Votes)
            .WithName("PutEventVote")
            .WithSummary("Vote for a suggestion")
            .WithDescription(
                "Idempotent: 201 the first time and 200 after, including when a concurrent request wrote the "
                + "vote first — the unique index is the arbiter. On a LocationAndTimeRestricted event the body "
                + "carries presence, and a missing or failed proof is 422. The coordinate is compared and "
                + "discarded; it is never stored and never logged.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapWithdraw(RouteGroupBuilder group) =>
        group.MapDelete("/{eventId:guid}/tracks/{suggestionId:guid}/vote", WithdrawAsync)
            .RequireAuthorization()
            .WithName("DeleteEventVote")
            .WithSummary("Take back a vote")
            .WithDescription(
                "The caller's own vote and no other. No presence proof is asked for: leaving the venue is not a "
                + "reason to be stuck having voted, and a withdrawal lowers a count rather than raising one.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapIssueCode(RouteGroupBuilder group) =>
        group.MapPost("/{eventId:guid}/event-codes", IssueCodeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(EventRateLimiterPolicies.Codes)
            .WithName("PostEventCode")
            .WithSummary("Issue a short-lived venue code")
            .WithDescription(
                "Owner only, and rate limited per account. The code is returned once and stored only as a "
                + "SHA-256 digest; it is single-use per account, so one code works for a whole room exactly "
                + "once each.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> VoteAsync(
        Guid eventId,
        Guid suggestionId,
        VoteRequest? request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        VoteWriteOutcome outcome = await sender.Send(
            new CastVoteCommand(eventId, suggestionId, request?.Presence?.ToSubmission()),
            cancellationToken);

        return outcome switch
        {
            VoteWriteOutcome.Recorded => Results.Created(
                $"/api/v1/events/{eventId}/tracks/{suggestionId}/vote",
                null),
            VoteWriteOutcome.Unchanged => Results.Ok(),

            // One answer for every way a proof can fail. Naming which part failed would tell a caller whether a
            // code exists, or how far outside the geofence they are — ADR-0014, SPEC-018 decision 6.
            VoteWriteOutcome.PresenceNotProved => Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["presence"] =
                        ["This event's licence requires proof of presence, and the proof was missing or not accepted."],
                },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> WithdrawAsync(
        Guid eventId,
        Guid suggestionId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new WithdrawVoteCommand(eventId, suggestionId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> IssueCodeAsync(
        Guid eventId,
        EventCodeRequest? request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        try
        {
            string? code = await sender.Send(
                new IssueEventCodeCommand(eventId, request?.TtlSeconds ?? DefaultTimeToLiveSeconds),
                cancellationToken);

            return code is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/events/{eventId}/event-codes", new { code });
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }


}

/// <summary>The vote payload. Empty unless the licence requires presence.</summary>
/// <param name="Presence">What the client offers as proof of being there.</param>
internal sealed record VoteRequest(PresenceRequest? Presence);

/// <summary>The evidence a voter supplies — `presence` in <c>docs/api/ENDPOINTS.md</c> §4.</summary>
/// <param name="Coordinates">Where the client says it is.</param>
/// <param name="EventCode">The code read at the venue.</param>
internal sealed record PresenceRequest(CoordinatesRequest? Coordinates, string? EventCode)
{
    /// <summary>The submission, as the verifier reads it.</summary>
    internal PresenceProofSubmission ToSubmission() =>
        new(Coordinates?.Latitude, Coordinates?.Longitude, EventCode);
}

/// <summary>A claimed position. Compared and discarded.</summary>
/// <param name="Latitude">The latitude, in degrees.</param>
/// <param name="Longitude">The longitude, in degrees.</param>
internal sealed record CoordinatesRequest(double Latitude, double Longitude);

/// <summary>The code-issuing payload.</summary>
/// <param name="TtlSeconds">How long the code should be accepted for.</param>
internal sealed record EventCodeRequest(int? TtlSeconds);
