using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Auth.Commands.LinkSocialAccount;

/// <summary>
/// Binds a provider identity to the signed-in account — subject V.1's second row, SPEC-041.
/// </summary>
/// <param name="Provider">The configuration row's name.</param>
/// <param name="Code">The authorization code the client came back with.</param>
/// <param name="CodeVerifier">The PKCE verifier matching the challenge.</param>
/// <param name="RedirectUri">The redirect URI the code was issued against.</param>
/// <remarks>
/// The same acquisition as signing in, and deliberately so: linking an identity the caller cannot prove they
/// hold would let anyone attach their own Google account to somebody else's MusicRoom account.
/// </remarks>
public sealed record LinkSocialAccountCommand(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri) : IRequest<SocialLinkResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "auth.social-link";

    /// <inheritdoc />
    public string AuditTargetType => "social-link";
}

/// <summary>What a link attempt produced.</summary>
/// <param name="Outcome">Linked, already linked, or refused by the provider.</param>
/// <param name="Provider">Which provider, when it linked.</param>
public sealed record SocialLinkResult(SocialLinkAttempt Outcome, string? Provider);

/// <summary>Why a link ended as it did.</summary>
public enum SocialLinkAttempt
{
    /// <summary>The identity is now bound to the caller's account.</summary>
    Linked = 0,

    /// <summary>The identity already signs somebody in, or this account already has that provider.</summary>
    AlreadyLinked = 1,

    /// <summary>No configuration row names that provider.</summary>
    UnknownProvider = 2,

    /// <summary>The provider refused the code, or the token failed a check.</summary>
    Refused = 3,
}
