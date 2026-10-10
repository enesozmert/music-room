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
    /// <param name="profile">The account's empty profile. Created with the account so no read path has to
    /// answer for a missing one — SPEC-011 escalation 4.</param>
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
        Profile profile,
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
    /// Reads the stored credential for an account, by id.
    /// </summary>
    /// <param name="userId">The account from the validated token.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The credential, or null when no such account exists or it is soft-deleted.</returns>
    /// <remarks>
    /// By id rather than by address, because the caller of a password change is already authenticated and the
    /// address is not part of that request — SPEC-028. The hash comes back so the handler can ask the hasher to
    /// verify against it; nothing returns it to a caller and nothing logs it.
    /// </remarks>
    Task<UserCredential?> FindCredentialByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an account's password hash and revokes every refresh family except one.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="newPasswordHash">The hash to store.</param>
    /// <param name="keepFamilyId">The refresh family to spare: the caller's own session, which the `sid`
    /// claim names — SPEC-010.</param>
    /// <param name="nowUtc">The server's clock.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the account existed and the write happened.</returns>
    /// <remarks>
    /// Both writes or neither, for the reason the reset path gives: a change that replaced the hash and failed
    /// to revoke would leave a thief holding a live session behind a password its owner had already changed.
    /// <para>
    /// The caller's own family is spared deliberately — ASVS V7.4.3 asks that the *other* sessions end, and
    /// logging somebody out of the device they just used to change their password is not a security gain.
    /// </para>
    /// </remarks>
    Task<bool> TryChangePasswordAsync(
        Guid userId,
        string newPasswordHash,
        Guid keepFamilyId,
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
    Task<PasswordResetResult> TryResetPasswordAsync(
        string tokenHash,
        string newPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// What a reset attempt did, and to which account.
/// </summary>
/// <param name="Outcome">Reset, expired, or invalid.</param>
/// <param name="UserId">The account whose password was replaced, or null when nothing was.</param>
/// <remarks>
/// The id is here so the audit record can name the account a successful reset acted on — SPEC-009. A failed
/// attempt carries null: a row naming an account for a token that was refused would say the token once
/// belonged to it, which is more than the caller proved.
/// <para>
/// Declared beside the interface rather than in its own file: it exists only as that method's return value,
/// and SPEC-009's Files table is the contract for what this change may create.
/// </para>
/// </remarks>
public sealed record PasswordResetResult(ResetPasswordOutcome Outcome, Guid? UserId);

/// <summary>
/// The three facts a login decision is made from.
/// </summary>
/// <param name="UserId">The account, which becomes the <c>sub</c> claim on success.</param>
/// <param name="PasswordHash">The stored BCrypt string the candidate is checked against.</param>
/// <param name="IsEmailVerified">False until the address is proven. An unverified account cannot use
/// the services — SDD §11.1.</param>
/// <param name="Email">The address, which the context screen reads the local part of — SPEC-035.</param>
/// <param name="DisplayName">The name other users see, screened for the same reason.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as that method's return
/// value, and SPEC-005's Files table is the contract for what this change may create.
/// <para>
/// **Why two columns a login does not need.** SPEC-035 screens a chosen password against the caller's own words,
/// and the change path has no other way to reach them: the access token carries `sub`, `sid` and `jti` and
/// nothing else, by design, and `ChangePasswordCommandHandler`'s constructor is at S107's five-parameter limit,
/// so a sixth dependency for a context reader would not compile under `-warnaserror`. Two more projected columns
/// cost nothing at a call site. The login path receives both and uses neither — it was given the address it is
/// looking the account up by.
/// </para>
/// </remarks>
public sealed record UserCredential(
    Guid UserId,
    string? PasswordHash,
    bool IsEmailVerified,
    string Email,
    string DisplayName);
