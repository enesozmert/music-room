using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Auth.Commands.AuthorizeClient;

/// <summary>
/// Issues an authorization code to a registered client, for the account that is already signed in — SPEC-044.
/// </summary>
/// <remarks>
/// **Four checks, in this order, and the order is the point.** The client first, then its redirect URI,
/// because every later refusal is reported *to* that URI and must not be sent anywhere unverified. Then the
/// response type, then the challenge.
/// <para>
/// Nothing here authenticates anybody. Under the owner's answer A the authorization endpoint sits behind
/// <c>RequireAuthorization</c>, so the account arrives from a token the middleware already validated. That is
/// what makes this a **first-party** provider, and ADR-0031 carries the sentence.
/// </para>
/// </remarks>
internal sealed class AuthorizeClientCommandHandler
    : IRequestHandler<AuthorizeClientCommand, AuthorizeClientResult>
{
    /// <summary>How long a code may be exchanged for — 60 seconds, SPEC-044 decision 3.</summary>
    /// <remarks>
    /// OpenID Connect Core 1.0 §3.1.3.2 asks for a lifetime of one minute or less. A code travels through a
    /// redirect into a client that exchanges it immediately; anything longer is a window for no benefit.
    /// </remarks>
    internal const int LifetimeSeconds = 60;

    /// <summary>What a request that names no scope is treated as having asked for.</summary>
    internal const string DefaultScope = "openid";

    private readonly IOpenIdClientRegistry _clients;
    private readonly IAuthorizationCodeRepository _codes;
    private readonly ICurrentUserAccessor _caller;
    private readonly IAuditContext _audit;
    private readonly TimeProvider _clock;

    /// <summary>Takes the client registry, the code store, the caller, the audit context and the clock.</summary>
    /// <param name="clients">The registered clients — configuration, not a table.</param>
    /// <param name="codes">Where a code and its binding are written.</param>
    /// <param name="caller">The account the validated bearer token names.</param>
    /// <param name="audit">Where this handler states what became of the request — SPEC-009.</param>
    /// <param name="clock">The instant the row is stamped with.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public AuthorizeClientCommandHandler(
        IOpenIdClientRegistry clients,
        IAuthorizationCodeRepository codes,
        ICurrentUserAccessor caller,
        IAuditContext audit,
        TimeProvider clock)
    {
        _clients = clients;
        _codes = codes;
        _caller = caller;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>Checks the four things, then issues.</summary>
    /// <param name="request">The parameters the client sent.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The code, or which check refused it.</returns>
    public async Task<AuthorizeClientResult> Handle(
        AuthorizeClientCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OpenIdClient? client = _clients.Find(request.ClientId);

        if (client is null)
        {
            return Refused(AuthorizeOutcome.UnknownClient);
        }

        if (!client.Allows(request.RedirectUri))
        {
            return Refused(AuthorizeOutcome.UnregisteredRedirectUri);
        }

        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
        {
            return Refused(AuthorizeOutcome.UnsupportedResponseType);
        }

        if (!ChallengeIsUsable(request))
        {
            return Refused(AuthorizeOutcome.InvalidChallenge);
        }

        // Behind RequireAuthorization, so a null here is a wiring fault rather than an anonymous caller. It
        // fails loudly: issuing a code for Guid.Empty would be issuing one for nobody.
        Guid userId = _caller.UserId
            ?? throw new InvalidOperationException(
                "The authorization endpoint requires a bearer token — SPEC-044, the owner's answer A.");

        return await IssueAsync(request, client, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the code and its binding, and names the account in the audit record.</summary>
    /// <remarks>
    /// Separated from <c>Handle</c> because the four checks above it and the issuing below it together pass
    /// the 40-line method limit the root .editorconfig enforces (S138). The split is along the seam the
    /// method already had: decide, then issue.
    /// </remarks>
    private async Task<AuthorizeClientResult> IssueAsync(
        AuthorizeClientCommand request,
        OpenIdClient client,
        Guid userId,
        CancellationToken cancellationToken)
    {
        string code = RefreshTokenSecret.Create();
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;

        await _codes.IssueAsync(
                new AuthorizationCode
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ClientId = client.ClientId,
                    RedirectUri = request.RedirectUri,
                    CodeHash = OpaqueTokenHash.Of(code),
                    CodeChallenge = request.CodeChallenge,
                    Nonce = request.Nonce,
                    Scope = string.IsNullOrWhiteSpace(request.Scope) ? DefaultScope : request.Scope,
                    AuthenticatedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddSeconds(LifetimeSeconds),
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                },
                cancellationToken)
            .ConfigureAwait(false);

        _audit.SetUserId(userId);
        _audit.SetTargetId(userId);

        return new AuthorizeClientResult(AuthorizeOutcome.Granted, code);
    }

    /// <summary>
    /// Whether the PKCE challenge is one this provider can check later.
    /// </summary>
    /// <remarks>
    /// The method must be <c>S256</c>, and the challenge must be the length an <c>S256</c> challenge has:
    /// base64url of a 32-byte digest is 43 characters. A challenge of the wrong shape is refused here rather
    /// than stored, because a stored challenge no verifier can ever match is a code that looks issued and is
    /// not redeemable.
    /// </remarks>
    private static bool ChallengeIsUsable(AuthorizeClientCommand request) =>
        string.Equals(request.CodeChallengeMethod, PkceChallenge.Method, StringComparison.Ordinal)
        && request.CodeChallenge.Length == PkceChallenge.ChallengeLength;

    private AuthorizeClientResult Refused(AuthorizeOutcome outcome)
    {
        _audit.SetOutcome(AuditOutcome.Refused);

        return AuthorizeClientResult.Refused(outcome);
    }
}
