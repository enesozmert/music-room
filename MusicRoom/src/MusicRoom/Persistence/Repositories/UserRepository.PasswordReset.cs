using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Auth.Commands.ResetPassword;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The code half of the password reset — SPEC-055.
/// </summary>
/// <remarks>
/// A third file of the same class rather than more lines in `UserRepository.cs`, which stands at the 300
/// the root .editorconfig allows — issue O27, and the same answer `UserRepository.Verification.cs` gave.
/// </remarks>
internal sealed partial class UserRepository
{
    /// <inheritdoc />
    public async Task<PasswordResetResult> TryResetPasswordByCodeAsync(
        string email,
        string codeHash,
        string newPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // All three writes or none, exactly as the token path: a reset that replaced the password and
        // failed to revoke the families would leave a thief holding a live session behind a password its
        // owner had already changed.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        Guid? account = await _context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account is not { } userId)
        {
            // An address nobody registered answers exactly as a wrong code does. The alternative is an
            // endpoint that says which addresses hold an account.
            return new PasswordResetResult(ResetPasswordOutcome.Invalid, UserId: null);
        }

        // One conditional UPDATE, and the budget is in its predicate: a row already at the limit does not
        // match, so the sixth attempt is refused even when the code is right.
        int consumedRows = await _context.PasswordResetTokens
            .Where(token => token.UserId == userId
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
            await ChargeResetAttemptAsync(userId, nowUtc, cancellationToken).ConfigureAwait(false);
            await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

            // Expired is not told apart from wrong here, unlike the token path. The token identifies its
            // own row, so a `410` for an expired one says nothing a holder of that token did not know; a
            // code is scoped by an address, and `410` for it would say that address has a reset
            // outstanding.
            return new PasswordResetResult(ResetPasswordOutcome.Invalid, UserId: null);
        }

        await ReplacePasswordHashAsync(userId, newPasswordHash, nowUtc, cancellationToken)
            .ConfigureAwait(false);
        await RevokeEveryRefreshTokenAsync(userId, nowUtc, cancellationToken).ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        // The id goes back so the audit record can name the account this reset acted on — SPEC-009.
        return new PasswordResetResult(ResetPasswordOutcome.Reset, userId);
    }

    /// <summary>
    /// Charges one attempt against every live reset row of an account.
    /// </summary>
    /// <remarks>
    /// Every live row rather than the one the code belongs to, because a wrong code belongs to none of
    /// them. An account has at most one live row — issuing supersedes the rest — so in practice this
    /// charges the row the person is looking at.
    /// </remarks>
    private Task<int> ChargeResetAttemptAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.PasswordResetTokens
            .Where(token => token.UserId == userId
                && token.ConsumedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.Attempts, token => token.Attempts + 1)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);
}
