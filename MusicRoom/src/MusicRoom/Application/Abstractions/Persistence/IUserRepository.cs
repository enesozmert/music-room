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
}
