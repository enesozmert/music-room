using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.UnlinkSocialAccount;

/// <summary>
/// Removes a provider identity from the caller's own account — SPEC-041.
/// </summary>
internal sealed class UnlinkSocialAccountCommandHandler
    : IRequestHandler<UnlinkSocialAccountCommand, SocialUnlinkOutcome>
{
    private readonly ISocialLinkRepository _links;
    private readonly ICurrentUserAccessor _caller;
    private readonly IAuditContext _audit;

    /// <summary>Takes the link store, the caller and the audit context.</summary>
    /// <param name="links">Where the binding lives.</param>
    /// <param name="caller">Who is signed in, from the validated token.</param>
    /// <param name="audit">Where the outcome is stated.</param>
    /// <remarks>Public on an internal type: MediatR activates through a public constructor only.</remarks>
    public UnlinkSocialAccountCommandHandler(
        ISocialLinkRepository links,
        ICurrentUserAccessor caller,
        IAuditContext audit)
    {
        _links = links;
        _caller = caller;
        _audit = audit;
    }

    /// <summary>Unbinds, unless it is the last way in.</summary>
    /// <param name="request">Which provider.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>What happened.</returns>
    public async Task<SocialUnlinkOutcome> Handle(
        UnlinkSocialAccountCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _caller.UserId ?? throw NoCaller();

        _audit.SetUserId(userId);

        SocialUnlinkOutcome outcome = await _links
            .TryUnlinkAsync(userId, request.Provider, cancellationToken)
            .ConfigureAwait(false);

        if (outcome != SocialUnlinkOutcome.Unlinked)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }

    private static InvalidOperationException NoCaller() =>
        new("UnlinkSocialAccountCommand reached its handler without an authenticated caller. "
            + "The route requires authorisation — SPEC-041.");
}
