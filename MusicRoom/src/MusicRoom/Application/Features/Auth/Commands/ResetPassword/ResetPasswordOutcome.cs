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
}
