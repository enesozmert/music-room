using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A single-use, 60-second proof that an authenticated account authorised one client — ADR-0031, SPEC-044.
/// </summary>
/// <remarks>
/// The row holds the <em>hash</em> of the code, never the code, for the reason the refresh token and the
/// verification token hold theirs: a leaked database must not hand anybody a working credential.
/// <para>
/// **The binding is the security of the flow.** A code is issued against one client, one redirect URI, one
/// PKCE challenge and one account, and the token endpoint refuses it unless all four agree. Without the
/// binding, a code intercepted on the way back to one client could be spent by another.
/// </para>
/// </remarks>
public sealed class AuthorizationCode : Entity
{
    /// <summary>The account that authorised the client. It becomes the ID token's <c>sub</c>.</summary>
    public Guid UserId { get; set; }

    /// <summary>The client the code was issued to. It becomes the ID token's <c>aud</c>.</summary>
    public required string ClientId { get; set; }

    /// <summary>The registered redirect URI the code was issued against, compared again on exchange.</summary>
    public required string RedirectUri { get; set; }

    /// <summary>The SHA-256 digest of the code, hex-encoded. The code itself is never stored.</summary>
    public required string CodeHash { get; set; }

    /// <summary>The PKCE <c>S256</c> challenge the verifier is checked against — <c>plain</c> is refused.</summary>
    public required string CodeChallenge { get; set; }

    /// <summary>The client's <c>nonce</c>, echoed into the ID token, or null when none was sent.</summary>
    public string? Nonce { get; set; }

    /// <summary>The scopes asked for, stored as sent so the token response can state what was granted.</summary>
    public required string Scope { get; set; }

    /// <summary>
    /// When the account proved itself, which becomes the ID token's <c>auth_time</c>.
    /// </summary>
    /// <remarks>
    /// Under the owner's answer A the authorization endpoint takes a bearer token, so the instant the account
    /// proved itself to <em>this</em> request is when it presented that token. It is stamped here rather than
    /// derived later, because a claim about when somebody authenticated must not be recomputed by whoever
    /// reads it.
    /// </remarks>
    public DateTime AuthenticatedAtUtc { get; set; }

    /// <summary>When the code stops being accepted. Always UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// When the code was spent, or null while it is unused. A second exchange is refused on this column.
    /// </summary>
    public DateTime? ConsumedAtUtc { get; set; }
}
