using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Queries.ListSocialLinks;

/// <summary>
/// Reads the caller's own links — SPEC-041.
/// </summary>
internal sealed class ListSocialLinksQueryHandler
    : IRequestHandler<ListSocialLinksQuery, IReadOnlyList<SocialLinkSummary>>
{
    private readonly ISocialLinkRepository _links;
    private readonly ICurrentUserAccessor _caller;

    /// <summary>Takes the link store and the caller.</summary>
    /// <param name="links">The read path.</param>
    /// <param name="caller">Who is signed in, from the validated token.</param>
    /// <remarks>Public on an internal type: MediatR activates through a public constructor only.</remarks>
    public ListSocialLinksQueryHandler(ISocialLinkRepository links, ICurrentUserAccessor caller)
    {
        _links = links;
        _caller = caller;
    }

    /// <summary>Lists them, oldest first.</summary>
    /// <param name="request">The query, which carries nothing.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The caller's links.</returns>
    public Task<IReadOnlyList<SocialLinkSummary>> Handle(
        ListSocialLinksQuery request,
        CancellationToken cancellationToken)
    {
        Guid userId = _caller.UserId
            ?? throw new InvalidOperationException(
                "ListSocialLinksQuery reached its handler without an authenticated caller. "
                + "The route requires authorisation — SPEC-041.");

        return _links.ListAsync(userId, cancellationToken);
    }
}
