using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// Signs access tokens with HS256, via the handler in
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> — owner decision, 2026-09-27, escalation 1
/// option B.
/// </summary>
/// <remarks>
/// Issuance only. Validating a token is the middleware's job and lands with the first authenticated
/// endpoint, which is logout; nothing here registers an authentication handler.
/// </remarks>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler _handler = new();

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    /// <summary>Takes the validated settings and prepares the signing credentials once.</summary>
    /// <param name="options">The settings the composition root read from configuration.</param>
    public JwtAccessTokenIssuer(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The payload is <c>sub</c> and <c>jti</c> plus the instants: no address, no display name, no
    /// role. A JWT is signed, not secret, and everything in it is readable by whoever holds it. The
    /// handler also stamps <c>iss</c>, <c>aud</c> and <c>nbf</c>, none of which says anything about
    /// the account beyond what <c>sub</c> already does.
    /// </remarks>
    public IssuedAccessToken Issue(Guid userId, DateTime nowUtc)
    {
        const int secondsPerMinute = 60;

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = nowUtc.AddMinutes(_options.AccessTokenMinutes),
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // The account this token speaks for, and a unique id per token so a single issued
                // token can be named in an audit entry without naming its bearer's secret.
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };

        return new IssuedAccessToken(
            _handler.CreateToken(descriptor),
            _options.AccessTokenMinutes * secondsPerMinute);
    }
}
