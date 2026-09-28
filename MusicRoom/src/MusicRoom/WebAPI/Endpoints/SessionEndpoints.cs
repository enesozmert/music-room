using MediatR;
using MusicRoom.Application.Features.Auth.Commands.EndSession;
using MusicRoom.Application.Features.Auth.Queries.ListSessions;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The authenticated session endpoints under <c>/api/v1/auth</c> — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <remarks>
/// Its own file rather than three more members on <see cref="AuthEndpoints"/>, which stands at 299 lines of
/// the 300 the root .editorconfig allows.
/// <para>
/// These are the first routes in this codebase that require authentication, and the first that answer
/// <c>404</c> rather than <c>403</c> for something the caller may not see — a <c>403</c> would confirm that
/// another account's session exists.
/// </para>
/// </remarks>
internal static class SessionEndpoints
{
    /// <summary>Maps the three routes onto the identity group.</summary>
    /// <param name="group">The <c>/api/v1/auth</c> route group.</param>
    internal static void Map(RouteGroupBuilder group)
    {
        MapEndCurrentSession(group);
        MapListSessions(group);
        MapEndSession(group);
    }

    private static void MapEndCurrentSession(RouteGroupBuilder group) =>
        group.MapDelete("/sessions/current", EndCurrentSessionAsync)
            .RequireAuthorization()
            .WithName("DeleteAuthCurrentSession")
            .WithSummary("Sign out")
            .WithDescription(
                "Revokes the refresh-token family this access token belongs to, named by its sid claim, so "
                + "no token from this session can be rotated again. The access token already issued stays "
                + "valid until it expires — at most 15 minutes and 30 seconds — which is the accepted window "
                + "in the threat register rather than an oversight.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapListSessions(RouteGroupBuilder group) =>
        group.MapGet("/sessions", ListSessionsAsync)
            .RequireAuthorization()
            .WithName("GetAuthSessions")
            .WithSummary("List your own sessions")
            .WithDescription(
                "Lists the caller's live sessions, newest first, each with the platform, device and app "
                + "version that started it so a session can be recognised. No token and no digest is "
                + "returned: the list exists to be read, not to be replayed.")
            .Produces<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapEndSession(RouteGroupBuilder group) =>
        group.MapDelete("/sessions/{sessionId:guid}", EndSessionAsync)
            .RequireAuthorization()
            .WithName("DeleteAuthSession")
            .WithSummary("End one of your sessions")
            .WithDescription(
                "Revokes one named session of the calling account. An unknown id, one already ended and one "
                + "belonging to another account all answer 404, because a distinct answer would confirm that "
                + "another account's session exists.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> EndCurrentSessionAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        Answer(await sender.Send(new EndSessionCommand(SessionId: null), cancellationToken));

    private static async Task<IResult> EndSessionAsync(
        Guid sessionId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Answer(await sender.Send(new EndSessionCommand(sessionId), cancellationToken));

    private static async Task<IResult> ListSessionsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SessionSummary> sessions = await sender.Send(new ListSessionsQuery(), cancellationToken);

        return Results.Ok(sessions.Select(ToResponse).ToList());
    }

    private static IResult Answer(EndSessionOutcome outcome) =>
        outcome == EndSessionOutcome.Ended
            ? Results.NoContent()
            : Results.NotFound();

    private static SessionResponse ToResponse(SessionSummary session) =>
        new(
            session.SessionId,
            session.Platform,
            session.Device,
            session.AppVersion,
            session.StartedAtUtc,
            session.ExpiresAtUtc);
}

/// <summary>
/// One session, as a client reads it — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="SessionId">The id to pass to <c>DELETE /auth/sessions/{sessionId}</c>.</param>
/// <param name="Platform">The client platform that started the session.</param>
/// <param name="Device">The device that started it.</param>
/// <param name="AppVersion">The app version that started it.</param>
/// <param name="StartedAt">When the session began.</param>
/// <param name="ExpiresAt">When it stops being renewable.</param>
internal sealed record SessionResponse(
    Guid SessionId,
    string Platform,
    string Device,
    string AppVersion,
    DateTime StartedAt,
    DateTime ExpiresAt);
