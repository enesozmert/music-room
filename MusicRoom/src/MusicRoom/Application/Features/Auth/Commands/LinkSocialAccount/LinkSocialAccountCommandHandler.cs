using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.LinkSocialAccount;

/// <summary>
/// Links a provider identity to the caller's own account — SPEC-041.
/// </summary>
/// <remarks>
/// The caller is read from the validated token and never from the request: a command that took a user id
/// would let anyone link an identity to anybody's account, which is the whole of subject V.6's gate on this
/// route.
/// </remarks>
internal sealed class LinkSocialAccountCommandHandler
    : IRequestHandler<LinkSocialAccountCommand, SocialLinkResult>
{
    private readonly ISocialIdentityProvider _identities;
    private readonly ISocialLinkRepository _links;
    private readonly ICurrentUserAccessor _caller;
    private readonly IAuditContext _audit;

    /// <summary>Takes the core, the link store, the caller and the audit context.</summary>
    /// <param name="identities">The relying-party core — ADR-0008.</param>
    /// <param name="links">Where the binding is written.</param>
    /// <param name="caller">Who is signed in, from the validated token.</param>
    /// <param name="audit">Where the outcome is stated — SPEC-009.</param>
    /// <remarks>Public on an internal type: MediatR activates through a public constructor only.</remarks>
    public LinkSocialAccountCommandHandler(
        ISocialIdentityProvider identities,
        ISocialLinkRepository links,
        ICurrentUserAccessor caller,
        IAuditContext audit)
    {
        _identities = identities;
        _links = links;
        _caller = caller;
        _audit = audit;
    }

    /// <summary>Authenticates with the provider, then binds.</summary>
    /// <param name="request">Which provider, and what the client came back with.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>What happened.</returns>
    public async Task<SocialLinkResult> Handle(
        LinkSocialAccountCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _caller.UserId ?? throw NoCaller();

        _audit.SetUserId(userId);

        SocialAuthentication authentication = await _identities
            .AuthenticateAsync(
                new SocialAuthenticationRequest(
                    request.Provider,
                    request.Code,
                    request.CodeVerifier,
                    request.RedirectUri),
                cancellationToken)
            .ConfigureAwait(false);

        if (authentication is not { Outcome: SocialAuthenticationOutcome.Authenticated, Identity: { } identity })
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return new SocialLinkResult(
                authentication.Outcome == SocialAuthenticationOutcome.UnknownProvider
                    ? SocialLinkAttempt.UnknownProvider
                    : SocialLinkAttempt.Refused,
                null);
        }

        SocialLinkOutcome outcome = await _links
            .TryLinkAsync(
                userId,
                identity.Provider,
                identity.ProviderUserId,
                identity.Email,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome == SocialLinkOutcome.AlreadyLinked)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return new SocialLinkResult(SocialLinkAttempt.AlreadyLinked, identity.Provider);
        }

        return new SocialLinkResult(SocialLinkAttempt.Linked, identity.Provider);
    }

    private static InvalidOperationException NoCaller() =>
        new("LinkSocialAccountCommand reached its handler without an authenticated caller. "
            + "The route requires authorisation — SPEC-041.");
}
