using MediatR;
using MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;
using MusicRoom.Application.Features.Profiles.Queries.SearchUsers;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Reading other accounts, under <c>/api/v1/users</c> — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <remarks>
/// Both routes are `auth`: an anonymous reader would make the `Public` tier a scraping surface. Neither returns
/// a field the caller may not see, and neither confirms whether an account exists.
/// </remarks>
public static class UserEndpoints
{
    /// <summary>The largest page a caller may ask for — an unbounded list is OWASP API4.</summary>
    public const int MaximumPageSize = 50;

    /// <summary>Maps the two routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/users");

        MapSearch(group);
        MapGetUser(group);

        return endpoints;
    }

    private static void MapSearch(RouteGroupBuilder group) =>
        group.MapGet("/", SearchAsync)
            .RequireAuthorization()
            .WithName("GetUsers")
            .WithSummary("Search accounts by display name")
            .WithDescription(
                "Matches a case-insensitive prefix of the display name, paged with a capped size. The display "
                + "name is the only field searched: matching a profile field would let a stranger confirm a "
                + "friends-only value by probing for it. A blank query returns nothing rather than everybody.")
            .Produces<IReadOnlyList<UserSearchResult>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapGetUser(RouteGroupBuilder group) =>
        group.MapGet("/{userId:guid}", GetUserAsync)
            .RequireAuthorization()
            .WithName("GetUser")
            .WithSummary("Read one account's profile")
            .WithDescription(
                "Returns the profile projected per field against the caller's relationship to its owner: self, "
                + "friend or stranger. A field the caller may not see is absent from the body and is never read "
                + "from the database, so a caller cannot tell a hidden field from an empty one. An account that "
                + "does not exist answers the same sparse shape as one whose owner hid everything, so asking "
                + "does not confirm existence.")
            .Produces<VisibleProfile>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static async Task<IResult> GetUserAsync(
        Guid userId,
        ISender sender,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetUserProfileQuery(userId), cancellationToken));

    private static async Task<IResult> SearchAsync(
        ISender sender,
        CancellationToken cancellationToken,
        string q = "",
        int page = 1,
        int pageSize = 20)
    {
        IReadOnlyList<UserSearchResult> results = await sender.Send(
            new SearchUsersQuery(q, page < 1 ? 1 : page, Size(pageSize)),
            cancellationToken);

        return Results.Ok(results);
    }

    private static int Size(int pageSize) =>
        Math.Min(Math.Max(pageSize, 1), MaximumPageSize);
}
