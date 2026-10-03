using FluentValidation;
using MediatR;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Application.Features.Catalog.Queries.GetTrack;
using MusicRoom.Application.Features.Catalog.Queries.SearchTracks;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The catalog under <c>/api/v1/tracks</c> — <c>docs/api/ENDPOINTS.md</c> §3, subject III.
/// </summary>
/// <remarks>
/// Both routes require a bearer token, because the YouTube key is ours and an anonymous search would make this
/// API a free proxy onto a quota the whole project shares.
/// <para>
/// `503` is the catalog's own answer, not a fault: the quota is spent or the provider is unreachable, and the
/// caller's request was fine. A `500` would claim otherwise (SPEC-025 decision 5).
/// </para>
/// </remarks>
public static class TrackEndpoints
{
    /// <summary>The default page size when a caller names none.</summary>
    public const int DefaultPageSize = 10;

    /// <summary>What a `503` says, so a client can tell it from an outage of this API.</summary>
    public const string UnavailableDetail =
        "The music catalog is unavailable. The provider could not be reached, or its daily quota is spent.";

    /// <summary>Maps the catalog routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    public static IEndpointRouteBuilder MapTrackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/tracks");

        MapSearch(group);
        MapGet(group);

        return endpoints;
    }

    private static void MapSearch(RouteGroupBuilder group) =>
        group.MapGet("/", SearchAsync)
            .RequireAuthorization()
            .RequireRateLimiting(CatalogRateLimiterPolicies.Searches)
            .WithName("GetTracks")
            .WithSummary("Search the catalog")
            .WithDescription(
                "The only route that can reach YouTube's expensive search call, so it is rate limited per "
                + "account and its results are cached. A repeated query inside the cache window costs no "
                + "quota at all.")
            .Produces<IReadOnlyList<TrackMetadata>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapGet(RouteGroupBuilder group) =>
        group.MapGet("/{youtubeVideoId}", GetAsync)
            .RequireAuthorization()
            .WithName("GetTrack")
            .WithSummary("Resolve one track")
            .WithDescription(
                "Prefers both cache levels, then one cheap metadata call. A video the catalog does not know "
                + "is 404, and so is a malformed id — the contract lists no 422 here, and one answer for both "
                + "keeps the route from saying which ids are well formed. A catalog that cannot answer at all "
                + "is 503.")
            .Produces<TrackMetadata>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> SearchAsync(
        ISender sender,
        string? q,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);

        SearchTracksQuery query = new(
            q ?? string.Empty,
            page ?? 1,
            pageSize ?? DefaultPageSize);

        try
        {
            CatalogResult<IReadOnlyList<TrackMetadata>> result = await sender
                .Send(query, cancellationToken)
                .ConfigureAwait(false);

            return result.Outcome == CatalogOutcome.Found && result.Value is not null
                ? Results.Ok(result.Value)
                : Unavailable();
        }
        catch (ValidationException exception)
        {
            // Mapped here, as on every other route: the validation behaviour throws, and the transport decides
            // what that means. A blank query must be `422` and not a hundred quota units spent on nothing.
            return Results.ValidationProblem(
                CredentialEndpointResults.ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<IResult> GetAsync(
        ISender sender,
        string youtubeVideoId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);

        CatalogResult<TrackMetadata> result = await sender
            .Send(new GetTrackQuery(youtubeVideoId), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            CatalogOutcome.Found when result.Value is not null => Results.Ok(result.Value),
            CatalogOutcome.Missing => Results.NotFound(),
            _ => Unavailable(),
        };
    }

    /// <summary>
    /// The `503` both routes answer when the catalog cannot be read.
    /// </summary>
    /// <remarks>
    /// A problem document with a type a client can branch on, and no provider detail: which provider failed and
    /// why is in the log, because a caller can act on neither.
    /// </remarks>
    private static IResult Unavailable() =>
        Results.Problem(
            detail: UnavailableDetail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Catalog unavailable");
}
