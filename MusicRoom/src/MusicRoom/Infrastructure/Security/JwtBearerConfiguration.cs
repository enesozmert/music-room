using Microsoft.IdentityModel.Tokens;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The rules a presented access token must satisfy — SDD §13.1, RFC 9700.
/// </summary>
/// <remarks>
/// Built here rather than in the composition root so the signing key's shape stays inside the project that
/// signs with it; `WebAPI` hands these parameters to `AddJwtBearer` without ever seeing the key twice.
/// <para>
/// **Asymmetric since SPEC-050.** The keys accepted are every key `ISigningKeyProvider` publishes — the
/// current one and, during a rotation overlap, the outgoing one, which is what SPEC-043 built the second
/// slot for. **No HMAC key is accepted**, not even for a window: an overlap would keep a shared secret that
/// still mints valid tokens, plus a knob to remember to turn off. The cost is that access tokens issued
/// before the deploy are refused once; refresh tokens are opaque and database-backed, so a client gets one
/// `401` and rotates.
/// </para>
/// </remarks>
public static class JwtBearerConfiguration
{
    /// <summary>
    /// The tolerance for clock drift — owner decision, 2026-09-28. Thirty seconds rather than the handler's
    /// five-minute default: a 15-minute token with five minutes of skew is a 20-minute token, which would
    /// silently make the post-revocation window in T-28 a third longer than the design states.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Builds the validation parameters for the configured issuer, audience and keys.</summary>
    /// <param name="options">The issuer, audience and lifetime the composition root read.</param>
    /// <param name="keys">The key pair this API signs with, whose public halves the JWKS publishes.</param>
    /// <returns>Parameters that accept exactly the tokens this application issued.</returns>
    public static TokenValidationParameters Build(JwtOptions options, ISigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keys);

        return new TokenValidationParameters
        {
            // Every one of these is on by default; they are written out because an authentication
            // configuration that relies on defaults is one upgrade away from being wrong in silence.
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = [.. keys.Verification.Select(material => material.Key)],
            ValidateLifetime = true,

            // A token with no expiry is not a session, it is a password. Refuse it even if signed.
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = ClockSkew,
        };
    }
}
