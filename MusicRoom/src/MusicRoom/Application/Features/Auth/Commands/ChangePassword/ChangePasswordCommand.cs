using MediatR;
using MusicRoom.Application.Abstractions.Auditing;

namespace MusicRoom.Application.Features.Auth.Commands.ChangePassword;

/// <summary>
/// Replaces the caller's password — `POST /api/v1/me/password`, SPEC-028.
/// </summary>
/// <param name="CurrentPassword">The password the caller is replacing. Proof that the person holding the
/// token also knows the credential, which is what ASVS V7.5.1 asks for before a sensitive attribute
/// changes.</param>
/// <param name="NewPassword">The replacement, held to the same rules registration applies.</param>
/// <remarks>
/// Auditable: subject V.6 wants every action recorded, and this is the one a user is most likely to ask about
/// afterwards. Neither password is ever logged, and the audit row names the account and no credential.
/// </remarks>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<ChangePasswordResult>, IAuditableCommand
{
    /// <inheritdoc />
    public string AuditAction => "account.password-change";

    /// <inheritdoc />
    public string AuditTargetType => "account";
}

/// <summary>
/// What became of a change attempt.
/// </summary>
/// <param name="Outcome">The outcome the endpoint turns into a status code.</param>
/// <param name="RetryAfter">How long to wait, when the outcome is <see cref="ChangePasswordOutcome.Throttled"/>.</param>
/// <remarks>
/// A value rather than an exception for the throttled case, for the reason SPEC-009 recorded: an exception on a
/// path the system expects took the audit row down with it.
/// </remarks>
public sealed record ChangePasswordResult(ChangePasswordOutcome Outcome, TimeSpan RetryAfter)
{
    /// <summary>The password was replaced.</summary>
    /// <returns>A changed result.</returns>
    public static ChangePasswordResult Changed() => new(ChangePasswordOutcome.Changed, TimeSpan.Zero);

    /// <summary>The current password did not match.</summary>
    /// <returns>A refused result.</returns>
    public static ChangePasswordResult Refused() => new(ChangePasswordOutcome.Refused, TimeSpan.Zero);

    /// <summary>The chosen password contains one of the account's own words, or the application's.</summary>
    /// <returns>A refused-by-context result.</returns>
    /// <remarks>
    /// Separate from <see cref="Refused"/> so the endpoint can answer with the `errors` body a client reads,
    /// keyed on `NewPassword`: a wrong **current** password is not a validation failure of the new one, and the
    /// two must not share a message — SPEC-035.
    /// </remarks>
    public static ChangePasswordResult RefusedByContext() =>
        new(ChangePasswordOutcome.RefusedByContext, TimeSpan.Zero);

    /// <summary>The account is out of attempts.</summary>
    /// <param name="retryAfter">How long the caller must wait.</param>
    /// <returns>A throttled result.</returns>
    public static ChangePasswordResult Throttled(TimeSpan retryAfter) =>
        new(ChangePasswordOutcome.Throttled, retryAfter);
}

/// <summary>
/// The four answers a change attempt can give.
/// </summary>
public enum ChangePasswordOutcome
{
    /// <summary>Replaced — `204`.</summary>
    Changed = 0,

    /// <summary>
    /// The current password was wrong — `422`.
    /// </summary>
    /// <remarks>
    /// Not `401`: the token is valid and the request is authenticated, and telling a client its session is over
    /// because of a typo sends a well-behaved client into a refresh-then-retry loop (SPEC-028 decision 3).
    /// </remarks>
    Refused = 1,

    /// <summary>Too many attempts — `429` with `Retry-After`.</summary>
    Throttled = 2,

    /// <summary>
    /// The new password contains one of the account's own words, or the application's — `422`, and nothing is
    /// written.
    /// </summary>
    /// <remarks>
    /// A returned value and never an exception: the audit behaviour is fail-closed and shares the handler's
    /// transaction, so a throw would roll back the row that records the refusal — SPEC-009, SPEC-035.
    /// </remarks>
    RefusedByContext = 3,
}
