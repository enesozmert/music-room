namespace MusicRoom.Application.Features.Auth.Commands.ExchangeAuthorizationCode;

/// <summary>
/// What the token endpoint produced for a code grant — SPEC-044.
/// </summary>
/// <param name="Outcome">Whether the exchange happened.</param>
/// <param name="Pair">The access and refresh tokens, on <see cref="TokenGrantOutcome.Granted"/>.</param>
/// <param name="IdToken">The RS256 ID token, on <see cref="TokenGrantOutcome.Granted"/>.</param>
/// <param name="Scope">The scopes granted, which are the ones the code was issued for.</param>
public sealed record ExchangeAuthorizationCodeResult(
    TokenGrantOutcome Outcome,
    AuthTokenPair? Pair,
    string? IdToken,
    string? Scope)
{
    /// <summary>A refusal, which carries no token.</summary>
    /// <param name="outcome">Which refusal.</param>
    /// <returns>The result.</returns>
    public static ExchangeAuthorizationCodeResult Refused(TokenGrantOutcome outcome) =>
        new(outcome, null, null, null);
}

/// <summary>
/// The three ways a code grant ends.
/// </summary>
/// <remarks>
/// The names are OAuth's error codes rather than this codebase's vocabulary, because the token endpoint's
/// error body is fixed by RFC 6749 §5.2 and is not ProblemDetails. ADR-0045's `code` member is for refusals
/// this API designed; these are refusals the specification already named.
/// </remarks>
public enum TokenGrantOutcome
{
    /// <summary>The code was spent and a pair was issued.</summary>
    Granted = 0,

    /// <summary>No configuration row names that client — <c>invalid_client</c>.</summary>
    InvalidClient = 1,

    /// <summary>
    /// The code was unknown, expired, already spent, bound to another client or another redirect URI, or
    /// the verifier did not match the challenge — <c>invalid_grant</c>.
    /// </summary>
    /// <remarks>
    /// Six causes, one answer. A client that could tell them apart could tell a live code from a spent one,
    /// and a wrong verifier from a wrong code, which is a guessing oracle for both.
    /// </remarks>
    InvalidGrant = 2,
}
