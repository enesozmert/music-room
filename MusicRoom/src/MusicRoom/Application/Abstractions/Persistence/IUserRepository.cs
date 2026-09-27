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
}
