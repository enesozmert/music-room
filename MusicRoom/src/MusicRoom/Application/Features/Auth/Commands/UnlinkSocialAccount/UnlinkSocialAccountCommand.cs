using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;

namespace MusicRoom.Application.Features.Auth.Commands.UnlinkSocialAccount;

/// <summary>
/// Unbinds a provider identity from the signed-in account — SPEC-041.
/// </summary>
/// <param name="Provider">Which provider to unlink.</param>
/// <remarks>
/// Refused when it is the account's only way in: an account with no password and one link would become an
/// account nobody can reach, including its owner. `docs/api/ENDPOINTS.md` publishes `409` for exactly that.
/// </remarks>
public sealed record UnlinkSocialAccountCommand(string Provider)
    : IRequest<SocialUnlinkOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "auth.social-unlink";

    /// <inheritdoc />
    public string AuditTargetType => "social-link";
}
