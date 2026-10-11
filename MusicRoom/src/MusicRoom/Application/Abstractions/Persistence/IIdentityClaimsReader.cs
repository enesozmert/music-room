namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// Reads the claims an identity assertion may carry — <c>GET /oauth2/userinfo</c>, SPEC-045.
/// </summary>
/// <remarks>
/// Its own interface rather than another method on <see cref="IUserRepository"/>, and its own implementation
/// file: `UserRepository.cs` stands at the 300 lines S104 allows (issue O27), which is the same reason
/// `PasswordContextRepository` exists beside it.
/// <para>
/// **It reads three columns and no hash.** Every other account read on this interface's neighbour answers a
/// <c>UserCredential</c>, which carries the stored password hash because signing in needs it. UserInfo does
/// not, so the projection here never materialises one — a read path that pulls a credential it has no use
/// for is a credential one logging mistake away from a response.
/// </para>
/// </remarks>
public interface IIdentityClaimsReader
{
    /// <summary>Reads one account's identity claims.</summary>
    /// <param name="userId">The account from the validated token's <c>sub</c>, never from a request.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The claims, or null when no such account exists — which a validated token makes impossible
    /// unless the account was deleted while its access token was still alive.</returns>
    Task<IdentityClaims?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// What this provider can assert about an account.
/// </summary>
/// <param name="Email">The address, as stored: trimmed and lower-cased.</param>
/// <param name="IsEmailVerified">Whether this API has proof of that address, which becomes
/// <c>email_verified</c>. A provider that asserted an unverified address as verified would be the
/// account-takeover path ADR-0008 names, from the other side.</param>
/// <param name="DisplayName">The name the account chose, which becomes <c>name</c>.</param>
/// <remarks>
/// Declared beside the interface because it exists only as its return value.
/// </remarks>
public sealed record IdentityClaims(string Email, bool IsEmailVerified, string DisplayName);
