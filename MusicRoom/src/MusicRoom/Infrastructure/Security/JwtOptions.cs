using System.Text;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The four settings an access token is signed with, from <c>.env</c> — SDD §13.1, ADR-0026.
/// </summary>
/// <remarks>
/// They travel as one record rather than as four parameters because
/// <see cref="InfrastructureServiceRegistration.AddInfrastructureServices"/> already carries three
/// arguments, and the .editorconfig limit is five. The constructor validates them, so a host that
/// starts has settings that can actually sign a token.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>
    /// The floor on the signing key, in bytes. HS256 keys shorter than the hash they feed weaken the
    /// signature, and <c>SymmetricSecurityKey</c> refuses them outright.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Validates the settings and keeps them.</summary>
    /// <param name="signingKey">The HS256 key, from <c>JWT_SIGNING_KEY</c>. Never logged, never
    /// included in a message thrown from here.</param>
    /// <param name="issuer">The <c>iss</c> claim, from <c>JWT_ISSUER</c>.</param>
    /// <param name="audience">The <c>aud</c> claim, from <c>JWT_AUDIENCE</c>.</param>
    /// <param name="accessTokenMinutes">The access-token lifetime, from
    /// <c>JWT_ACCESS_TOKEN_MINUTES</c>. 15 is the value SDD §13.1 and authentication-authorization
    /// fix; it is configured rather than compiled in, and there is no default.</param>
    /// <exception cref="ArgumentException">A setting is missing, the key is shorter than
    /// <see cref="MinimumSigningKeyBytes"/>, or the lifetime is not positive.</exception>
    public JwtOptions(string signingKey, string issuer, string audience, int accessTokenMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(accessTokenMinutes, 0);

        // The message names the variable and never its value: a short key is still a key.
        if (string.IsNullOrWhiteSpace(signingKey)
            || Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            throw new ArgumentException(
                $"JWT_SIGNING_KEY must be at least {MinimumSigningKeyBytes} bytes. Generate a random "
                + "value per environment — see .env.example and ADR-0026.",
                nameof(signingKey));
        }

        SigningKey = signingKey;
        Issuer = issuer;
        Audience = audience;
        AccessTokenMinutes = accessTokenMinutes;
    }

    /// <summary>The HS256 signing key.</summary>
    public string SigningKey { get; }

    /// <summary>The <c>iss</c> claim every issued token carries.</summary>
    public string Issuer { get; }

    /// <summary>The <c>aud</c> claim every issued token carries.</summary>
    public string Audience { get; }

    /// <summary>The access-token lifetime, in minutes.</summary>
    public int AccessTokenMinutes { get; }
}
