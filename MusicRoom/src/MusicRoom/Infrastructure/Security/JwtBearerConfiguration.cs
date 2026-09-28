using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The rules a presented access token must satisfy — SDD §13.1, RFC 9700.
/// </summary>
/// <remarks>
/// Built here rather than in the composition root so the signing key's shape stays inside the project that
/// signs with it; `WebAPI` hands these parameters to `AddJwtBearer` without ever seeing the key twice.
/// </remarks>
public static class JwtBearerConfiguration
{
    /// <summary>
    /// The tolerance for clock drift — owner decision, 2026-09-28. Thirty seconds rather than the handler's
    /// five-minute default: a 15-minute token with five minutes of skew is a 20-minute token, which would
    /// silently make the post-revocation window in T-28 a third longer than the design states.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Builds the validation parameters for the configured issuer, audience and key.</summary>
    /// <param name="options">The settings the composition root read from configuration.</param>
    /// <returns>Parameters that accept exactly the tokens this application issued.</returns>
    public static TokenValidationParameters Build(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenValidationParameters
        {
            // Every one of these is on by default; they are written out because an authentication
            // configuration that relies on defaults is one upgrade away from being wrong in silence.
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateLifetime = true,

            // A token with no expiry is not a session, it is a password. Refuse it even if signed.
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = ClockSkew,
        };
    }
}
