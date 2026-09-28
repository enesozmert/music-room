using System.Globalization;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// Proves the SDD §13.1 token properties against the real handler: what the token says, how long it
/// lives, and that the signature is over the configured key.
/// </summary>
public sealed class JwtAccessTokenIssuerTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string SigningKey = "a-test-signing-key-of-at-least-32-bytes";
    internal const string Issuer = "musicroom-tests";
    internal const string Audience = "musicroom-tests-client";
    internal const int AccessTokenMinutes = 15;

    /// <summary>The refresh-token family a test token belongs to — it becomes the `sid` claim.</summary>
    internal static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime _nowUtc = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonWebTokenHandler _handler = new();

    /// <summary>The payload is the account and the instants, and nothing that identifies a person.</summary>
    [Fact]
    public void Issue_CarriesSubIatExpAndJti_AndNothingSensitive()
    {
        // Arrange
        Guid userId = Guid.NewGuid();

        // Act
        JsonWebToken token = Issue(userId);

        // Assert
        Assert.Equal(userId.ToString(), token.Subject);
        Assert.NotEmpty(token.Id);
        Assert.Equal(Issuer, token.Issuer);
        Assert.Contains(Audience, token.Audiences, StringComparer.Ordinal);

        // A JWT is signed, not secret: whoever holds it reads it.
        Assert.DoesNotContain(
            token.Claims,
            claim => claim.Type is "email" or "name" or "display_name" or "password_hash");
    }

    /// <summary>
    /// The token names the session it belongs to, which is what lets logout end this one and no other —
    /// SPEC-010 escalation 1.
    /// </summary>
    [Fact]
    public void Issue_CarriesTheSessionIdAsTheSidClaim()
    {
        // Act
        JsonWebToken token = Issue(Guid.NewGuid());

        // Assert
        Assert.Equal(SessionId.ToString(), token.GetClaim("sid").Value);
    }

    /// <summary>15 minutes, from the instant the caller passed in — SDD §13.1.</summary>
    [Fact]
    public void Issue_ExpiresFifteenMinutesAfterTheIssuedInstant()
    {
        // Act
        JsonWebToken token = Issue(Guid.NewGuid());

        // Assert
        Assert.Equal(_nowUtc, token.IssuedAt);
        Assert.Equal(_nowUtc.AddMinutes(AccessTokenMinutes), token.ValidTo);
    }

    /// <summary>The lifetime the caller is told matches the lifetime that was signed.</summary>
    [Fact]
    public void Issue_ReportsTheSameLifetimeItSigned()
    {
        // Arrange
        const int secondsPerMinute = 60;
        JwtAccessTokenIssuer issuer = new(Options());

        // Act
        IssuedAccessToken issued = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), _nowUtc);

        // Assert
        Assert.Equal(AccessTokenMinutes * secondsPerMinute, issued.ExpiresInSeconds);
    }

    /// <summary>The signature is over the configured key, so a token signed elsewhere is not ours.</summary>
    [Fact]
    public async Task Issue_ProducesATokenThatValidatesUnderTheConfiguredKeyOnlyAsync()
    {
        // Arrange
        JwtAccessTokenIssuer issuer = new(Options());
        IssuedAccessToken issued = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        // Act
        TokenValidationResult accepted = await _handler.ValidateTokenAsync(issued.Token, Parameters(SigningKey));
        TokenValidationResult refused = await _handler.ValidateTokenAsync(
            issued.Token,
            Parameters("a-different-signing-key-of-32-plus-bytes"));

        // Assert
        Assert.True(accepted.IsValid);
        Assert.False(refused.IsValid);
    }

    /// <summary>A key too short for HS256 fails the constructor, not a later request.</summary>
    [Fact]
    public void JwtOptions_WithAKeyBelowTheFloor_Throws()
    {
        // Act
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new JwtOptions("too-short", Issuer, Audience, AccessTokenMinutes));

        // Assert — the message names the variable and the floor, never a value.
        Assert.Contains("JWT_SIGNING_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            JwtOptions.MinimumSigningKeyBytes.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A lifetime of zero or less would issue tokens that are already expired.</summary>
    [Fact]
    public void JwtOptions_WithANonPositiveLifetime_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JwtOptions(SigningKey, Issuer, Audience, 0));

    private static JwtOptions Options() => new(SigningKey, Issuer, Audience, AccessTokenMinutes);

    private static JsonWebToken Issue(Guid userId)
    {
        JwtAccessTokenIssuer issuer = new(Options());

        return _handler.ReadJsonWebToken(issuer.Issue(userId, SessionId, _nowUtc).Token);
    }

    private static TokenValidationParameters Parameters(string signingKey) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
    };
}
