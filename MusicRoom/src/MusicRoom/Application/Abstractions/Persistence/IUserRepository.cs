using MusicRoom.Application.Features.Auth.Commands.ResetPassword;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The write path for accounts. Interfaces live here and implementations in <c>Persistence</c>, so
/// <c>Application</c> never learns about EF Core — ef-core-persistence.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Inserts an account together with its verification token, in one transaction.
    /// </summary>
    /// <param name="user">The account to create.</param>
    /// <param name="verificationToken">The token issued for that account.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when the row was written, false when <c>ux_users_email</c> refused it because the
    /// address is already registered. The duplicate is a return value rather than an exception
    /// because it is an expected outcome, and because the database — not a prior read — is what
    /// arbitrates the race.
    /// </returns>
    Task<bool> TryRegisterAsync(
        User user,
        EmailVerificationToken verificationToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Spends a verification token and marks the owning account verified, in one transaction.
    /// </summary>
    /// <param name="tokenHash">The digest of the submitted token. The caller hashes it; the
    /// plaintext never reaches this layer, so no implementation of this interface can log it.</param>
    /// <param name="nowUtc">The instant the request is judged against, used for both the expiry
    /// comparison and the <c>consumed_at_utc</c> stamp.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// <see cref="VerifyEmailOutcome.Verified"/> only for the single caller that won the token.
    /// Single use is arbitrated by the <c>consumed_at_utc IS NULL</c> predicate in the database, not
    /// by a read-then-write check here — the same reasoning as <c>ux_users_email</c>.
    /// </returns>
    Task<VerifyEmailOutcome> TryConsumeEmailVerificationTokenAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues a replacement verification token for an unverified account, superseding any link
    /// already outstanding for it, in one transaction.
    /// </summary>
    /// <param name="email">The address to match, already trimmed and lower-cased by the caller —
    /// the column stores it that way and this method does not normalise it again.</param>
    /// <param name="token">The token to write. Its <see cref="EmailVerificationToken.UserId"/> is
    /// assigned by the implementation, because the account an address belongs to is resolved inside
    /// the same transaction that inserts the row rather than by an earlier read.</param>
    /// <param name="nowUtc">The instant the superseded rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when the token was written. False means there is no unverified account for the address
    /// — deliberately without saying whether the address is unknown or its account is already
    /// verified, because the caller answers <c>202</c> to both and must not be able to tell them
    /// apart (SPEC-004, owner decision 3).
    /// </returns>
    /// <remarks>
    /// Superseding is the owner's decision of 2026-09-27: exactly one link is live at a time, so the
    /// guess surface does not grow with the number of resends. The cost is that a link from an
    /// earlier mail stops working.
    /// </remarks>
    Task<bool> TryIssueEmailVerificationTokenAsync(
        string email,
        EmailVerificationToken token,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads what signing in needs to judge a password, and nothing else.
    /// </summary>
    /// <param name="email">The address to match, already trimmed and lower-cased by the caller —
    /// the column stores it that way and this method does not normalise it again.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The credential, or null when no account holds the address.</returns>
    /// <remarks>
    /// A narrow projection rather than the <see cref="User"/> aggregate: the login path has no
    /// reason to hold a tracked entity, and a record of three fields cannot accidentally be logged
    /// whole with a display name and an address attached to the hash.
    /// </remarks>
    Task<UserCredential?> FindCredentialByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a password reset token for an account, superseding any reset token already outstanding for
    /// it, in one transaction.
    /// </summary>
    /// <param name="email">The address to match, already trimmed and lower-cased by the caller.</param>
    /// <param name="token">The token to write. Its <see cref="PasswordResetToken.UserId"/> is assigned by
    /// the implementation, because the account an address belongs to is resolved inside the same
    /// transaction that inserts the row rather than by an earlier read.</param>
    /// <param name="nowUtc">The instant the superseded rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when the token was written. False means no account holds the address — deliberately without
    /// saying so any other way, because the caller answers <c>202</c> to both.
    /// </returns>
    /// <remarks>
    /// Superseding follows SPEC-004's decision for verification tokens: exactly one link is live at a
    /// time, so the guess surface does not grow with the number of requests. Unlike verification, an
    /// unverified account is still eligible — someone who mistyped a password before confirming their
    /// address must still be able to recover it.
    /// </remarks>
    Task<bool> TryIssuePasswordResetTokenAsync(
        string email,
        PasswordResetToken token,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Spends a reset token, replaces the password hash, and revokes every refresh-token family the
    /// account holds — all three in one transaction.
    /// </summary>
    /// <param name="tokenHash">The digest of the submitted token. The caller hashes it; the plaintext
    /// never reaches this layer, so no implementation of this interface can log it.</param>
    /// <param name="newPasswordHash">The complete BCrypt string to store. Already hashed: this layer
    /// never sees a password.</param>
    /// <param name="nowUtc">The instant the request is judged against, used for the expiry comparison and
    /// for every stamp this method writes.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// <see cref="ResetPasswordOutcome.Reset"/> only for the single caller that won the token. Single use
    /// is arbitrated by the <c>consumed_at_utc IS NULL</c> predicate in the database, not by a
    /// read-then-write check here.
    /// </returns>
    /// <remarks>
    /// The three writes are inseparable by design — SPEC-007 escalation 1, option A, owner decision of
    /// 2026-09-27. A reset that replaced the password and failed to revoke the families would leave a
    /// thief holding a live session behind a password its owner had already changed. This is the one
    /// method that writes another aggregate's table, and the next multi-aggregate write reconsiders an
    /// explicit unit of work rather than widening this one.
    /// <para>
    /// It does **not** revoke access tokens already issued: a JWT is validated by signature and
    /// <c>exp</c>, so one minted before the reset stays valid for up to its remaining minutes. The
    /// 15-minute lifetime is the accepted mitigation — SDD §13.1, and the threat register says so.
    /// </para>
    /// </remarks>
    Task<ResetPasswordOutcome> TryResetPasswordAsync(
        string tokenHash,
        string newPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// The three facts a login decision is made from.
/// </summary>
/// <param name="UserId">The account, which becomes the <c>sub</c> claim on success.</param>
/// <param name="PasswordHash">The stored BCrypt string the candidate is checked against.</param>
/// <param name="IsEmailVerified">False until the address is proven. An unverified account cannot use
/// the services — SDD §11.1.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as that method's return
/// value, and SPEC-005's Files table is the contract for what this change may create.
/// </remarks>
public sealed record UserCredential(Guid UserId, string PasswordHash, bool IsEmailVerified);
