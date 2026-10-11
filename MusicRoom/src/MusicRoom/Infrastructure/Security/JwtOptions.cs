namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The three settings an access token is issued with, from <c>.env</c> — SDD §13.1, ADR-0026.
/// </summary>
/// <remarks>
/// They travel as one record rather than as three parameters because
/// <see cref="InfrastructureServiceRegistration.AddInfrastructureServices"/> already carries its
/// arguments as one options record, and the .editorconfig limit is five.
/// <para>
/// **There is no signing key here any more** — SPEC-050, closing O32. The key is an RSA pair read by
/// <see cref="RsaSigningKeyProvider"/>, which has its own floor (2048 bits) and its own refusal, and whose
/// public half is published at `/.well-known/jwks.json`. ADR-0031 removed the HMAC option; until SPEC-050
/// the access token was the last thing still using it.
/// </para>
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Validates the settings and keeps them.</summary>
    /// <param name="issuer">The <c>iss</c> claim, from <c>JWT_ISSUER</c>.</param>
    /// <param name="audience">The <c>aud</c> claim, from <c>JWT_AUDIENCE</c>.</param>
    /// <param name="accessTokenMinutes">The access-token lifetime, from
    /// <c>JWT_ACCESS_TOKEN_MINUTES</c>. 15 is the value SDD §13.1 and authentication-authorization
    /// fix; it is configured rather than compiled in, and there is no default.</param>
    /// <exception cref="ArgumentException">The issuer or the audience is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is not positive.</exception>
    public JwtOptions(string issuer, string audience, int accessTokenMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(accessTokenMinutes, 0);

        Issuer = issuer;
        Audience = audience;
        AccessTokenMinutes = accessTokenMinutes;
    }

    /// <summary>The <c>iss</c> claim every issued token carries.</summary>
    public string Issuer { get; }

    /// <summary>The <c>aud</c> claim every issued token carries.</summary>
    public string Audience { get; }

    /// <summary>The access-token lifetime, in minutes.</summary>
    public int AccessTokenMinutes { get; }
}
