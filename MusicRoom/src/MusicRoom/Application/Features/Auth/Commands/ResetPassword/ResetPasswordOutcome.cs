namespace MusicRoom.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// What an attempted reset did — the endpoint turns each value into one status code.
/// </summary>
public enum ResetPasswordOutcome
{
    /// <summary>
    /// Unknown, malformed or already spent. The three are deliberately one outcome: a caller holding a
    /// token that was spent must not learn that it once existed.
    /// </summary>
    Invalid = 0,

    /// <summary>The password was replaced and every refresh-token family was revoked.</summary>
    Reset = 1,

    /// <summary>The token existed, was never spent, and is past its one-hour window.</summary>
    Expired = 2,

    /// <summary>
    /// The chosen password contains one of the account's own words, or the application's — `422`, and the
    /// token is **not** spent.
    /// </summary>
    /// <remarks>
    /// A value rather than an exception, because the audit behaviour is fail-closed and shares this handler's
    /// transaction: a throw would take the row recording the refusal with it — SPEC-009 paid for that lesson.
    /// The screen runs before the write for the same kind of reason: a refused password that had already spent
    /// the caller's only reset link would be a worse answer than no answer — SPEC-035, ADR-0042.
    /// </remarks>
    RefusedByContext = 3,
}
