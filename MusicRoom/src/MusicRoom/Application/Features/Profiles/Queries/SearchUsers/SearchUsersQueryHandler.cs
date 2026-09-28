using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Profiles.Queries.SearchUsers;

/// <summary>
/// Finds accounts whose display name starts with the query — `auth` tier, paged.
/// </summary>
internal sealed class SearchUsersQueryHandler
    : IRequestHandler<SearchUsersQuery, IReadOnlyList<UserSearchResult>>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="profiles">Where accounts are searched.</param>
    /// <param name="currentUser">Who is searching, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public SearchUsersQueryHandler(IProfileRepository profiles, ICurrentUserAccessor currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    /// <summary>Searches, or returns nothing for a blank query.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The matching accounts, display name and id only.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public Task<IReadOnlyList<UserSearchResult>> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "SearchUsersQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-013.");

        // A blank query would return every account one page at a time, which is enumeration with extra steps.
        return string.IsNullOrWhiteSpace(request.Query)
            ? Task.FromResult<IReadOnlyList<UserSearchResult>>([])
            : _profiles.SearchByDisplayNameAsync(
                request.Query.Trim(),
                request.Page,
                request.PageSize,
                cancellationToken);
    }
}
