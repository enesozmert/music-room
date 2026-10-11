using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;

namespace MusicRoom.Infrastructure.Identity;

/// <summary>
/// Signs ID tokens with RS256, using the key pair SPEC-043 published — ADR-0031, SPEC-044 decision 4.
/// </summary>
/// <remarks>
/// **RS256, and the `kid` of the current key.** A verifier that has never spoken to this API reads the
/// discovery document, fetches the JWKS and checks the signature; the header's `kid` is what tells it which
/// of the published keys to use, which is what makes a rotation survivable.
/// <para>
/// **The claim list is ADR-0031's, exactly**: `iss`, `sub`, `aud`, `exp`, `iat`, `auth_time`, and `nonce`
/// when the client sent one. No address and no display name: an ID token is signed, not secret, and it
/// travels through a redirect. What a client wants to know about the person it just signed in belongs to the
/// UserInfo endpoint, where a scope decides it — SPEC-045.
/// </para>
/// </remarks>
public sealed class IdTokenIssuer : IIdTokenIssuer
{
    /// <summary>How long an ID token stays valid — five minutes.</summary>
    /// <remarks>
    /// An ID token is read once, immediately after the exchange, to learn who signed in. It is not a
    /// credential for calling anything, so it needs no lifetime beyond the moment it is validated; a long
    /// one only widens the window in which a copy can be replayed against a client.
    /// </remarks>
    public const int LifetimeMinutes = 5;

    private static readonly JsonWebTokenHandler _handler = new();

    private readonly ISigningKeyProvider _keys;

    /// <summary>Takes the key pair this API signs with.</summary>
    /// <param name="keys">The provider SPEC-043 registered. Its current key signs; the previous one, during
    /// a rotation overlap, is published and verified against but never signs.</param>
    public IdTokenIssuer(ISigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _keys = keys;
    }

    /// <inheritdoc />
    public string Issue(IdTokenRequest request, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        SigningKeyMaterial current = _keys.Current;

        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Sub] = request.UserId.ToString(),

            // Seconds since the epoch, which is what `auth_time` is defined as — OpenID Connect Core 1.0
            // §2. Written as a number rather than a formatted instant, because a client parses it.
            ["auth_time"] = new DateTimeOffset(request.AuthenticatedAtUtc, TimeSpan.Zero)
                .ToUnixTimeSeconds(),
        };

        // Echoed, never invented: the client sent this to bind the token it gets back to the request it
        // made, and a value this API chose would prove nothing to it — OpenID Connect Core 1.0 §3.1.2.1.
        if (!string.IsNullOrEmpty(request.Nonce))
        {
            claims["nonce"] = request.Nonce;
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = request.Issuer,
            Audience = request.ClientId,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = nowUtc.AddMinutes(LifetimeMinutes),
            SigningCredentials = new SigningCredentials(current.Key, SecurityAlgorithms.RsaSha256),
            Claims = claims,
        };

        // The `kid` header is written by the handler from the key's own identifier, which
        // `RsaSigningKeyProvider` sets to the thumbprint the JWKS publishes. Setting it here as well is
        // refused outright — `IDX14116: 'AdditionalHeaderClaims' cannot contain ... 'alg, kid, ...'`.
        return _handler.CreateToken(descriptor);
    }
}
