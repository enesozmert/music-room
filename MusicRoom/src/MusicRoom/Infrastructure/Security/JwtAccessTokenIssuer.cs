using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// Signs access tokens with **RS256**, using the key pair SPEC-043 published — ADR-0031, SPEC-050.
/// </summary>
/// <remarks>
/// Issuance only. Validating a token is the middleware's job; nothing here registers an authentication
/// handler.
/// <para>
/// **RS256 since SPEC-050, which closed O32.** It was HS256 until then, which ADR-0031 had already decided
/// against — *"a shared secret cannot be published in a JWKS, and a JWKS is what makes the provider
/// verifiable by anything other than itself."* The ID token moved first (SPEC-044) because changing the
/// signature of the token `AddJwtBearer` validates, in the unit that introduced a new flow, would have been
/// two migrations in one diff. This is the second one, on its own.
/// </para>
/// <para>
/// The `kid` header is written by the handler from the key's own identifier, which
/// <see cref="RsaSigningKeyProvider" /> sets to the thumbprint the JWKS publishes. A verifier therefore
/// picks the right key out of the set instead of trying each one — and a token signed before a rotation
/// still names the key that signed it.
/// </para>
/// </remarks>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler _handler = new();

    private readonly JwtOptions _options;
    private readonly ISigningKeyProvider _keys;

    /// <summary>Takes the validated settings and the key pair to sign with.</summary>
    /// <param name="options">The issuer, audience and lifetime the composition root read.</param>
    /// <param name="keys">The RS256 pair — SPEC-043. Its current key signs; the previous one, during a
    /// rotation overlap, is published and verified against and never signs.</param>
    public JwtAccessTokenIssuer(JwtOptions options, ISigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keys);

        _options = options;
        _keys = keys;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The payload is <c>sub</c>, <c>sid</c> and <c>jti</c> plus the instants: no address, no display name,
    /// no role. A JWT is signed, not secret, and everything in it is readable by whoever holds it. The
    /// handler also stamps <c>iss</c>, <c>aud</c> and <c>nbf</c>, none of which says anything about
    /// the account beyond what <c>sub</c> already does.
    /// </remarks>
    public IssuedAccessToken Issue(Guid userId, Guid sessionId, DateTime nowUtc)
    {
        const int secondsPerMinute = 60;

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = nowUtc.AddMinutes(_options.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(_keys.Current.Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // The account this token speaks for, and a unique id per token so a single issued
                // token can be named in an audit entry without naming its bearer's secret.
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),

                // The refresh-token family, so logout can end this session and leave the others —
                // SPEC-010. It is stable across a rotation, because a rotation continues the same session.
                [JwtRegisteredClaimNames.Sid] = sessionId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };

        return new IssuedAccessToken(
            _handler.CreateToken(descriptor),
            _options.AccessTokenMinutes * secondsPerMinute);
    }
}
