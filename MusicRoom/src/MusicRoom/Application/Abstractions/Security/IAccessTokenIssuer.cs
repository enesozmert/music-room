namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// Mints the short-lived access token half of a session — SDD §13.1.
/// </summary>
/// <remarks>
/// Declared here, implemented in <c>Infrastructure</c> — ADR-0028. The interface names neither the
/// algorithm nor the library, so replacing either touches one project. Validating a token is not on
/// this interface: it belongs to the middleware, and nothing authenticates yet.
/// </remarks>
public interface IAccessTokenIssuer
{
    /// <summary>Issues an access token for an account.</summary>
    /// <param name="userId">The account the token speaks for. It becomes the <c>sub</c> claim.</param>
    /// <param name="sessionId">The refresh-token family this token belongs to. It becomes the <c>sid</c>
    /// claim, which is what lets logout end <em>this</em> session and no other — SPEC-010 escalation 1.</param>
    /// <param name="nowUtc">The instant the token is issued at, used for <c>iat</c> and <c>exp</c>.
    /// Passed in rather than read inside, so a test can assert the window instead of racing it.</param>
    /// <returns>The signed token and the lifetime, in seconds, a client should plan for.</returns>
    /// <remarks>
    /// The token carries <c>sub</c>, <c>sid</c>, <c>iat</c>, <c>exp</c> and <c>jti</c> and nothing else.
    /// No address, no display name: a JWT is signed, not secret, and <c>sid</c> is an opaque id whose only
    /// meaning is "this session".
    /// </remarks>
    IssuedAccessToken Issue(Guid userId, Guid sessionId, DateTime nowUtc);
}

/// <summary>
/// What <see cref="IAccessTokenIssuer.Issue"/> produced.
/// </summary>
/// <param name="Token">The signed token, ready to be returned to the caller.</param>
/// <param name="ExpiresInSeconds">How long the token stays valid, for the client's own planning.
/// The server never trusts this number; it trusts the <c>exp</c> claim it signed.</param>
public sealed record IssuedAccessToken(string Token, int ExpiresInSeconds);
