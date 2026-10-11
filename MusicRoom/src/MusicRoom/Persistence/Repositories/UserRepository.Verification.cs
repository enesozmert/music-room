using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The code half of email verification, and the welcome stamp — SPEC-052.
/// </summary>
/// <remarks>
/// A second file of the same class rather than more lines in `UserRepository.cs`, which stands at the 300
/// the root .editorconfig allows — issue O27. A partial keeps one interface and one implementation while
/// letting the file-size rule do its job, which is the answer three units in a row have now given that wall.
/// </remarks>
internal sealed partial class UserRepository
{
    /// <summary>
    /// How many wrong codes a verification row survives — SPEC-052 decision 3.
    /// </summary>
    /// <remarks>
    /// Five. With a 32-symbol alphabet and six characters, five guesses are about one chance in 215
    /// million; the sixth attempt is refused whatever it says, and the person asks for a new message
    /// through an endpoint that is rate-limited per IP and per account.
    /// </remarks>
    internal const int MaximumCodeAttempts = 5;

    /// <inheritdoc />
    public async Task<VerifyEmailOutcome> TryConsumeEmailVerificationCodeAsync(
        string email,
        string codeHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        Guid? userId = await _context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userId is not { } account)
        {
            // An address nobody registered answers exactly as a wrong code does. The alternative is an
            // endpoint that says which addresses exist.
            return VerifyEmailOutcome.Invalid;
        }

        // One conditional UPDATE, and the budget is in its predicate: a row already at the limit does not
        // match, so the sixth attempt is refused even when the code is right.
        int consumedRows = await _context.EmailVerificationTokens
            .Where(token => token.UserId == account
                && token.CodeHash == codeHash
                && token.ConsumedAtUtc == null
                && token.ExpiresAtUtc > nowUtc
                && token.Attempts < MaximumCodeAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAtUtc, nowUtc)

                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context
                    // would have stamped is stamped here — SDD §8.3.
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (consumedRows == 0)
        {
            await ChargeAttemptAsync(account, nowUtc, cancellationToken).ConfigureAwait(false);

            return VerifyEmailOutcome.Invalid;
        }

        await MarkVerifiedAsync(account, nowUtc, cancellationToken).ConfigureAwait(false);

        return VerifyEmailOutcome.Verified;
    }

    /// <inheritdoc />
    public async Task<bool> TryStampWelcomeAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // `welcome_sent_at_utc IS NULL` in the predicate is what makes once mean once: two sign-ins that
        // race both run this statement, and exactly one of them affects a row.
        int stamped = await _context.Users
            .Where(user => user.Id == userId && user.WelcomeSentAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.WelcomeSentAtUtc, nowUtc)
                    .SetProperty(user => user.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        return stamped > 0;
    }

    /// <summary>
    /// Charges one attempt against every live verification row of an account.
    /// </summary>
    /// <remarks>
    /// Every live row rather than the one the code belongs to, because a wrong code belongs to none of
    /// them. An account has at most one live row — the resend path supersedes — so in practice this
    /// charges the row the person is looking at.
    /// </remarks>
    private Task<int> ChargeAttemptAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        _context.EmailVerificationTokens
            .Where(token => token.UserId == userId
                && token.ConsumedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.Attempts, token => token.Attempts + 1)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>Sets the account's verified flag, by id rather than by token digest.</summary>
    private Task<int> MarkVerifiedAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.IsEmailVerified, true)
                    .SetProperty(user => user.UpdatedAtUtc, nowUtc),
                cancellationToken);
}
