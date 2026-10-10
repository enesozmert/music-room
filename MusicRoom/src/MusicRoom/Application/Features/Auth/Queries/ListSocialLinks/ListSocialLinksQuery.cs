using MediatR;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Auth.Queries.ListSocialLinks;

/// <summary>
/// The provider identities bound to the signed-in account — SPEC-041.
/// </summary>
/// <remarks>
/// No parameter: the account is the caller's, read from the validated token. A query that took a user id
/// would be a route for reading somebody else's linked accounts, which subject V.6 refuses.
/// </remarks>
public sealed record ListSocialLinksQuery : IRequest<IReadOnlyList<SocialLinkSummary>>;
