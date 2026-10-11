using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.ExchangeAuthorizationCode;

/// <summary>
/// Spends an authorization code once and answers with a session and an ID token — SPEC-044.
/// </summary>
/// <remarks>
/// **The code is spent before the verifier is checked, deliberately.** The database arbitrates single use, so
/// the exchange happens first and the PKCE comparison second; a wrong verifier therefore burns the code. That
/// is the safe direction: it leaves no live code to guess a verifier against, and RFC 6749 §4.1.2 already
/// requires a code to be refused after one use. The cost is that a client with a verifier bug has to restart
/// the flow, which is a 60-second code and one redirect.
/// <para>
/// The session it issues is S1's session — <c>SessionIssuer</c>, one refresh-token family, rotation and
/// family revocation unchanged. ADR-0032's theft detection is not duplicated for this flow; it is reused by
/// it.
/// </para>
/// </remarks>
internal sealed class ExchangeAuthorizationCodeCommandHandler
    : IRequestHandler<ExchangeAuthorizationCodeCommand, ExchangeAuthorizationCodeResult>
{
    private readonly IOpenIdClientRegistry _clients;
    private readonly IAuthorizationCodeRepository _codes;
    private readonly SessionIssuer _sessions;
    private readonly IIdTokenIssuer _idTokens;
    private readonly IAuditContext _audit;

    /// <summary>Takes the registry, the code store, the session issuer, the ID token issuer and the audit context.</summary>
    /// <param name="clients">The registered clients.</param>
    /// <param name="codes">Where the code is spent.</param>
    /// <param name="sessions">Opens the refresh-token family and mints the pair, as password sign-in does.</param>
    /// <param name="idTokens">Signs the assertion that says who authenticated.</param>
    /// <param name="audit">Where this handler states what became of the exchange — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only. Five
    /// parameters is the limit the root .editorconfig enforces (S107), and the clock is not among them: the
    /// instants here come from the repository's own judgement and from the session issuer.
    /// </remarks>
    public ExchangeAuthorizationCodeCommandHandler(
        IOpenIdClientRegistry clients,
        IAuthorizationCodeRepository codes,
        SessionIssuer sessions,
        IIdTokenIssuer idTokens,
        IAuditContext audit)
    {
        _clients = clients;
        _codes = codes;
        _sessions = sessions;
        _idTokens = idTokens;
        _audit = audit;
    }

    /// <summary>Checks the client, spends the code, checks the verifier, then issues.</summary>
    /// <param name="request">What the client presented.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The pair and the ID token, or one of two refusals.</returns>
    public async Task<ExchangeAuthorizationCodeResult> Handle(
        ExchangeAuthorizationCodeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OpenIdClient? client = _clients.Find(request.ClientId);

        if (client is null)
        {
            return Refused(TokenGrantOutcome.InvalidClient);
        }

        DateTime nowUtc = DateTime.UtcNow;

        // The client and the redirect URI are part of the predicate rather than checked after the read: a
        // code issued to another client simply does not match, so there is no window in which it could.
        AuthorizationCodeRedemption redemption = await _codes
            .TryConsumeAsync(
                OpaqueTokenHash.Of(request.Code),
                client.ClientId,
                request.RedirectUri,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        if (!redemption.Spent || !PkceChallenge.Matches(redemption.CodeChallenge, request.CodeVerifier))
        {
            return Refused(TokenGrantOutcome.InvalidGrant);
        }

        _audit.SetUserId(redemption.UserId);
        _audit.SetTargetId(redemption.UserId);

        AuthTokenPair pair = await _sessions
            .IssueAsync(redemption.UserId, redemption.Scope, cancellationToken)
            .ConfigureAwait(false);

        string idToken = _idTokens.Issue(
            new IdTokenRequest(
                request.Issuer,
                client.ClientId,
                redemption.UserId,
                redemption.AuthenticatedAtUtc,
                redemption.Nonce),
            nowUtc);

        return new ExchangeAuthorizationCodeResult(
            TokenGrantOutcome.Granted,
            pair,
            idToken,
            redemption.Scope);
    }

    private ExchangeAuthorizationCodeResult Refused(TokenGrantOutcome outcome)
    {
        _audit.SetOutcome(AuditOutcome.Refused);

        return ExchangeAuthorizationCodeResult.Refused(outcome);
    }
}
