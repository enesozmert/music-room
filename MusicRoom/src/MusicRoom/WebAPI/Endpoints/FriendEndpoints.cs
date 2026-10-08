using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Friends.Commands.CancelFriendRequest;
using MusicRoom.Application.Features.Friends.Commands.RemoveFriend;
using MusicRoom.Application.Features.Friends.Commands.RespondToFriendRequest;
using MusicRoom.Application.Features.Friends.Commands.SendFriendRequest;
using MusicRoom.Application.Features.Friends.Queries.ListFriendRequests;
using MusicRoom.Application.Features.Friends.Queries.ListFriends;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The friends graph under <c>/api/v1/me</c> — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <remarks>
/// Every route is authenticated, and the caller comes from the token. A request or friendship that is not the
/// caller's answers <c>404</c>, never <c>403</c>: a distinct answer would confirm it exists.
/// </remarks>
internal static class FriendEndpoints
{
    /// <summary>The largest page a caller may ask for — an unbounded list is OWASP API4.</summary>
    internal const int MaximumPageSize = 100;

    /// <summary>Maps the six routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapFriendEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/me");

        MapListFriends(group);
        MapRemoveFriend(group);
        MapListRequests(group);
        MapSendRequest(group);
        MapRespondToRequest(group);
        MapCancelRequest(group);

        return endpoints;
    }

    private static void MapListFriends(RouteGroupBuilder group) =>
        group.MapGet("/friends", ListFriendsAsync)
            .RequireAuthorization()
            .WithName("GetMeFriends")
            .WithSummary("List your friends")
            .WithDescription(
                "Lists the caller's friends, newest first, paged. A friendship is symmetric: both parties see "
                + "each other here, and neither can see it without the other.")
            .Produces<IReadOnlyList<FriendResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapRemoveFriend(RouteGroupBuilder group) =>
        group.MapDelete("/friends/{userId:guid}", RemoveFriendAsync)
            .RequireAuthorization()
            .WithName("DeleteMeFriend")
            .WithSummary("Remove a friend")
            .WithDescription(
                "Ends the friendship for both sides at once, and clears the request rows between the pair so "
                + "they may befriend again. Answers 404 when there is no friendship to end.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static void MapListRequests(RouteGroupBuilder group) =>
        group.MapGet("/friend-requests", ListRequestsAsync)
            .RequireAuthorization()
            .WithName("GetMeFriendRequests")
            .WithSummary("List your pending friend requests")
            .WithDescription(
                "Lists pending requests in one direction: incoming are the ones addressed to the caller, "
                + "outgoing the ones the caller sent. The direction is required, because the two screens that "
                + "show these lists show one each.")
            .Produces<IReadOnlyList<FriendRequestResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapSendRequest(RouteGroupBuilder group) =>
        group.MapPost("/friend-requests", SendRequestAsync)
            .RequireAuthorization()
            .WithName("PostMeFriendRequest")
            .WithSummary("Ask somebody to be friends")
            .WithDescription(
                "Creates a pending request. Answers 409 when the pair are already friends, when a request is "
                + "already pending in either direction, or when the caller asks themselves; 404 when no such "
                + "account exists. A crossing request is not auto-accepted: nobody becomes a friend without "
                + "accepting.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapRespondToRequest(RouteGroupBuilder group) =>
        group.MapPatch("/friend-requests/{requestId:guid}", RespondToRequestAsync)
            .RequireAuthorization()
            .WithName("PatchMeFriendRequest")
            .WithSummary("Accept or decline a friend request")
            .WithDescription(
                "Only the recipient may answer. Accepting creates the friendship in the same transaction that "
                + "marks the request accepted, so twenty simultaneous accepts produce one friendship. An "
                + "unknown request, one already answered and one addressed to somebody else all answer 404.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapCancelRequest(RouteGroupBuilder group) =>
        group.MapDelete("/friend-requests/{requestId:guid}", CancelRequestAsync)
            .RequireAuthorization()
            .WithName("DeleteMeFriendRequest")
            .WithSummary("Withdraw a friend request you sent")
            .WithDescription(
                "Only the sender may withdraw; a recipient declines instead, which is a different verb and a "
                + "different row state. The row is deleted rather than marked, so the pair may try again.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> ListFriendsAsync(
        ISender sender,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        IReadOnlyList<FriendSummary> friends = await sender.Send(
            new ListFriendsQuery(Page(page), Size(pageSize)),
            cancellationToken);

        return Results.Ok(friends
            .Select(friend => new FriendResponse(friend.UserId, friend.DisplayName, friend.FriendsSinceUtc))
            .ToList());
    }

    private static async Task<IResult> ListRequestsAsync(
        ISender sender,
        CancellationToken cancellationToken,
        string direction = "incoming",
        int page = 1,
        int pageSize = 20)
    {
        if (!Enum.TryParse(direction, ignoreCase: true, out FriendRequestDirection parsed))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["direction"] = ["direction must be 'incoming' or 'outgoing'."],
                },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        IReadOnlyList<FriendRequestSummary> requests = await sender.Send(
            new ListFriendRequestsQuery(parsed, Page(page), Size(pageSize)),
            cancellationToken);

        return Results.Ok(requests
            .Select(request => new FriendRequestResponse(
                request.RequestId,
                request.UserId,
                request.DisplayName,
                request.RequestedAtUtc))
            .ToList());
    }

    private static async Task<IResult> SendRequestAsync(
        FriendRequestCreation request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        SendFriendRequestResult result;

        try
        {
            result = await sender.Send(new SendFriendRequestCommand(request.TargetUserId), cancellationToken);
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }

        return result.Status switch
        {
            SendFriendRequestStatus.Created => Results.Created(
                $"/api/v1/me/friend-requests/{result.RequestId}",
                value: null),
            SendFriendRequestStatus.RecipientNotFound => Results.NotFound(),
            _ => Results.Conflict(),
        };
    }

    private static async Task<IResult> RespondToRequestAsync(
        Guid requestId,
        FriendRequestDecision decision,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(decision.Status, "accepted", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(decision.Status, "declined", StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["status"] = ["status must be 'accepted' or 'declined'."],
                },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        bool accept = string.Equals(decision.Status, "accepted", StringComparison.OrdinalIgnoreCase);

        bool answered = await sender.Send(
            new RespondToFriendRequestCommand(requestId, accept),
            cancellationToken);

        return answered ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> CancelRequestAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new CancelFriendRequestCommand(requestId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> RemoveFriendAsync(
        Guid userId,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new RemoveFriendCommand(userId), cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static int Page(int page) => page < 1 ? 1 : page;

    private static int Size(int pageSize) =>
        pageSize is < 1 or > MaximumPageSize ? Math.Min(Math.Max(pageSize, 1), MaximumPageSize) : pageSize;
}

/// <summary>The friend request payload — <c>docs/api/ENDPOINTS.md</c> §2.</summary>
/// <param name="TargetUserId">The account being asked.</param>
internal sealed record FriendRequestCreation(Guid TargetUserId);

/// <summary>The response payload for accepting or declining.</summary>
/// <param name="Status">`accepted` or `declined`.</param>
internal sealed record FriendRequestDecision(string Status);

/// <summary>One friend, as a client reads them.</summary>
/// <param name="UserId">Their account id.</param>
/// <param name="DisplayName">Their handle.</param>
/// <param name="FriendsSince">When the friendship began.</param>
internal sealed record FriendResponse(Guid UserId, string DisplayName, DateTime FriendsSince);

/// <summary>One pending request, as a client reads it.</summary>
/// <param name="RequestId">The id the respond and cancel routes take.</param>
/// <param name="UserId">The other party.</param>
/// <param name="DisplayName">Their handle.</param>
/// <param name="RequestedAt">When it was sent.</param>
internal sealed record FriendRequestResponse(
    Guid RequestId,
    Guid UserId,
    string DisplayName,
    DateTime RequestedAt);
