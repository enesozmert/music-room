using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth.Commands.Login;

/// <summary>
/// What a sign-in attempt produced — SPEC-009 escalation 2.
/// </summary>
/// <param name="Pair">The token pair on success, null on every failure.</param>
/// <param name="Throttled">Set when the account is out of attempts, so the endpoint answers `429` with
/// <c>Retry-After</c> instead of `401`. Null otherwise — including for a wrong password, which is a `401`.</param>
/// <remarks>
/// Both null means refused: unknown address, wrong password or unverified account, which answer identically
/// by design. The distinction the endpoint needs is only "refused" versus "out of attempts"; why a refusal
/// happened is in the audit record, not in the response.
/// </remarks>
public sealed record LoginResult(AuthTokenPair? Pair, CredentialThrottle? Throttled)
{
    /// <summary>The refusal every failing credential path answers with.</summary>
    public static LoginResult Refused { get; } = new(null, null);

    /// <summary>A refusal because the account is out of attempts.</summary>
    /// <param name="throttle">The wait the limiter reported.</param>
    /// <returns>The result the endpoint turns into `429`.</returns>
    public static LoginResult OutOfAttempts(CredentialThrottle throttle) => new(null, throttle);

    /// <summary>A successful sign-in.</summary>
    /// <param name="pair">The issued pair.</param>
    /// <returns>The result the endpoint turns into `200`.</returns>
    public static LoginResult Issued(AuthTokenPair pair) => new(pair, null);
}
