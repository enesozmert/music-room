namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// Signs the ID token — the assertion that says who authenticated, ADR-0031 and SPEC-044 decision 4.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>Infrastructure</c>, as <see cref="IAccessTokenIssuer"/> is: the
/// interface names neither the algorithm nor the library.
/// <para>
/// **An ID token is not an access token and is never accepted in its place.** This one is RS256, verifiable
/// against the published JWKS by something that has never spoken to this API; the access token is the
/// credential that authorises a call. ADR-0031 keeps them separate, and so does this interface.
/// </para>
/// </remarks>
public interface IIdTokenIssuer
{
    /// <summary>Issues an ID token for one authentication of one account by one client.</summary>
    /// <param name="request">Who authenticated, for which client, and against which issuer.</param>
    /// <param name="nowUtc">The instant the token is issued at, used for <c>iat</c> and <c>exp</c>.
    /// Passed in rather than read inside, so a test can assert the window instead of racing it.</param>
    /// <returns>The signed compact JWT.</returns>
    string Issue(IdTokenRequest request, DateTime nowUtc);
}

/// <summary>
/// Everything an ID token asserts beyond the instants.
/// </summary>
/// <param name="Issuer">This provider's identifier, which is the origin the discovery document was read
/// from. Passed in because it is a property of the request's host, and <c>Application</c> does not read
/// <c>HttpContext</c>.</param>
/// <param name="ClientId">The client the token is for. It becomes <c>aud</c>.</param>
/// <param name="UserId">The account that authenticated. It becomes <c>sub</c>.</param>
/// <param name="AuthenticatedAtUtc">When that account proved itself. It becomes <c>auth_time</c>.</param>
/// <param name="Nonce">The value the client bound its own request with, echoed rather than invented, or
/// null when the client sent none.</param>
/// <remarks>
/// A record rather than five parameters: five is the limit the root .editorconfig enforces (S107), and a
/// claim set is one thing.
/// </remarks>
public sealed record IdTokenRequest(
    string Issuer,
    string ClientId,
    Guid UserId,
    DateTime AuthenticatedAtUtc,
    string? Nonce);
