namespace MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>
/// What happened when a verification token was spent — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <remarks>
/// Three losing cases — unknown, malformed and already consumed — collapse into
/// <see cref="Invalid"/> on purpose. A distinct answer for "already consumed" would tell an
/// anonymous caller that a token it does not hold once existed, which is the enumeration leak the
/// whole flow is built to avoid. Owner decision, 2026-09-26, recorded in SPEC-003.
/// </remarks>
public enum VerifyEmailOutcome
{
    /// <summary>The token was unknown, malformed or already spent. Answered with <c>400</c>.</summary>
    Invalid = 0,

    /// <summary>The token was spent by this caller and the account is now verified. <c>204</c>.</summary>
    Verified = 1,

    /// <summary>The token exists, was never spent, and its window has closed. <c>410</c>.</summary>
    Expired = 2,
}
