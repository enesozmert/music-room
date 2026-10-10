using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.SignInWithSocial;

/// <summary>
/// Signs in — or registers — with a provider identity, subject V.1, SPEC-041.
/// </summary>
/// <param name="Provider">The configuration row's name: `google` today, `facebook` next.</param>
/// <param name="Code">The authorization code the client came back from the provider with.</param>
/// <param name="CodeVerifier">The PKCE verifier matching the challenge the client sent.</param>
/// <param name="RedirectUri">The redirect URI the code was issued against.</param>
/// <remarks>
/// The code is exchanged by this API, never by the client — ADR-0008's B shape, the owner's answer of
/// 2026-10-10. What the client holds is a code and a verifier, neither of which is a credential on its own.
/// </remarks>
public sealed record SignInWithSocialCommand(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri) : IRequest<SignInWithSocialResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "auth.social-sign-in";

    /// <inheritdoc />
    public string AuditTargetType => "session";
}
