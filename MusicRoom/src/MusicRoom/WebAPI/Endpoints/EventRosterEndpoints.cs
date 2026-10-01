using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events.Commands.AttendEvent;
using MusicRoom.Application.Features.Events.Commands.InviteToEvent;
using MusicRoom.Application.Features.Events.Commands.LeaveEvent;
using MusicRoom.Application.Features.Events.Commands.UninviteFromEvent;
using MusicRoom.Application.Features.Events.Queries.ListInvitations;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The roster routes of an event — <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <remarks>
/// Invitations are the owner's, and on a private event an invitation **is** the `member` gate. Attendance is
/// the caller's own and is a roster, not a gate: subject V.2.1 says that when an event is public, all users can
/// find it and vote, so requiring an attendance row first would narrow that to whoever remembered to check in.
/// <para>
/// Its own class rather than five more members on <see cref="EventEndpoints"/>, which would put that file past
/// the 300-line limit the root .editorconfig enforces.
/// </para>
/// </remarks>
internal static class EventRosterEndpoints
{
    /// <summary>Maps the five routes onto the event group.</summary>
    /// <param name="group">The <c>/api/v1/events</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapListInvitations(group);
        MapInvite(group);
        MapUninvite(group);
        MapAttend(group);
        MapLeave(group);
    }

    private static void MapListInvitations(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}/invitations", ListAsync)
            .RequireAuthorization()
            .WithName("GetEventInvitations")
            .WithSummary("List who was invited")
            .WithDescription(
                "Owner only. Who else was invited to a private event is not a participant's business, and an "
                + "empty list for a non-owner would still confirm the event exists — so the answer is 404.")
            .Produces<IReadOnlyList<InvitationSummary>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapInvite(RouteGroupBuilder group) =>
        group.MapPost("/{eventId:guid}/invitations", InviteAsync)
            .RequireAuthorization()
            .WithName("PostEventInvitation")
            .WithSummary("Invite somebody to an event")
            .WithDescription(
                "Owner only. A duplicate is 409, decided by the unique index rather than by a read-then-write "
                + "check. Inviting the owner is also 409: they already hold the strongest grant.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

    private static void MapUninvite(RouteGroupBuilder group) =>
        group.MapDelete("/{eventId:guid}/invitations/{userId:guid}", UninviteAsync)
            .RequireAuthorization()
            .WithName("DeleteEventInvitation")
            .WithSummary("Withdraw an invitation")
            .WithDescription(
                "Owner only. The gate is read live on every request, so the invitee loses access on their next "
                + "call rather than on their next sign-in.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapAttend(RouteGroupBuilder group) =>
        group.MapPost("/{eventId:guid}/attendances", AttendAsync)
            .RequireAuthorization()
            .WithName("PostEventAttendance")
            .WithSummary("Join an event's roster")
            .WithDescription(
                "Any authenticated caller on a public event, an invitee on a private one. This is a roster and "
                + "never a precondition: voting does not require it.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

    private static void MapLeave(RouteGroupBuilder group) =>
        group.MapDelete("/{eventId:guid}/attendances/current", LeaveAsync)
            .RequireAuthorization()
            .WithName("DeleteEventAttendance")
            .WithSummary("Leave an event's roster")
            .WithDescription(
                "`current` rather than a user id: the only attendance a caller may remove is their own.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> ListAsync(
        Guid eventId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InvitationSummary>? invitations = await sender.Send(
            new ListInvitationsQuery(eventId),
            cancellationToken);

        return invitations is null ? Results.NotFound() : Results.Ok(invitations);
    }

    private static async Task<IResult> InviteAsync(
        Guid eventId,
        InvitationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        RosterWriteOutcome outcome = await sender.Send(
            new InviteToEventCommand(eventId, request.UserId),
            cancellationToken);

        return Answer(outcome, $"/api/v1/events/{eventId}/invitations");
    }

    private static async Task<IResult> UninviteAsync(
        Guid eventId,
        Guid userId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new UninviteFromEventCommand(eventId, userId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> AttendAsync(
        Guid eventId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        RosterWriteOutcome outcome = await sender.Send(
            new AttendEventCommand(eventId),
            cancellationToken);

        return Answer(outcome, $"/api/v1/events/{eventId}/attendances/current");
    }

    private static async Task<IResult> LeaveAsync(
        Guid eventId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new LeaveEventCommand(eventId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    /// <summary>The one status a roster write answers with.</summary>
    private static IResult Answer(RosterWriteOutcome outcome, string location) => outcome switch
    {
        RosterWriteOutcome.Added => Results.Created(location, null),
        RosterWriteOutcome.Conflict => Results.Conflict(),
        _ => Results.NotFound(),
    };
}

/// <summary>The invitation payload.</summary>
/// <param name="UserId">Who to invite.</param>
internal sealed record InvitationRequest(Guid UserId);
