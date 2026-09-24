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
}
