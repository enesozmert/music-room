using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Auth.Commands.ResetPassword;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Npgsql;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core write path for accounts.
/// </summary>
internal sealed partial class UserRepository : IUserRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a
    /// public constructor only, and an internal one leaves <see cref="IUserRepository"/> unresolvable.
    /// </remarks>
    public UserRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<bool> TryRegisterAsync(
        User user,
        EmailVerificationToken verificationToken,
        Profile profile,
        CancellationToken cancellationToken)
    {
        _context.Users.Add(user);
        _context.EmailVerificationTokens.Add(verificationToken);
        _context.Profiles.Add(profile);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsEmailAlreadyRegistered(exception))
        {
            // The database arbitrated the race and refused this writer. Detach both entries so the
            // context is not left holding rows it failed to write.
            _context.Entry(user).State = EntityState.Detached;
            _context.Entry(verificationToken).State = EntityState.Detached;
            _context.Entry(profile).State = EntityState.Detached;

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<VerifyEmailOutcome> TryConsumeEmailVerificationTokenAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // The spend and the flag must not be separable. A crash between them would leave a token
        // consumed and its account unverified, and no later request could repair that: the only
        // token that could set the flag is the one already spent.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        // One conditional UPDATE. Whoever affects a row won the token; everybody else is told so by
        // the database, under its own row lock, rather than by a read this code performed earlier.
        int consumedRows = await _context.EmailVerificationTokens
            .Where(token => token.TokenHash == tokenHash
                && token.ConsumedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAtUtc, nowUtc)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (consumedRows == 0)
        {
            return await ClassifyLostRaceAsync(tokenHash, nowUtc, cancellationToken).ConfigureAwait(false);
        }

        await MarkEmailVerifiedAsync(tokenHash, nowUtc, cancellationToken).ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        return VerifyEmailOutcome.Verified;
    }

    /// <inheritdoc />
    public async Task<bool> TryIssueEmailVerificationTokenAsync(
        string email,
        EmailVerificationToken token,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        // The supersede and the insert must not be separable. A crash between them would leave an
        // account with every link consumed and no replacement, which no later request could repair
        // except by asking for yet another resend.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        Guid userId = await _context.Users
            .AsNoTracking()
            .Where(user => user.Email == email && !user.IsEmailVerified)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userId == Guid.Empty)
        {
            return false;
        }

        await SupersedeOutstandingTokensAsync(userId, nowUtc, cancellationToken).ConfigureAwait(false);

        token.UserId = userId;
        _context.EmailVerificationTokens.Add(token);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public Task<UserCredential?> FindCredentialByEmailAsync(
        string email,
        CancellationToken cancellationToken) =>
        _context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            // Projected in the database, so the hash arrives without the display name and the
            // address that would make an accidental log entry a profile.
            .Select(user => new UserCredential(
                user.Id, user.PasswordHash, user.IsEmailVerified, user.Email, user.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<UserCredential?> FindCredentialByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            // Projected in the database, as the by-address read is, so the hash arrives without the display
            // name and the address that would make an accidental log entry a profile.
            .Select(user => new UserCredential(
                user.Id, user.PasswordHash, user.IsEmailVerified, user.Email, user.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryChangePasswordAsync(
        Guid userId,
        string newPasswordHash,
        Guid keepFamilyId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Both writes or neither — the reset path's reasoning, for the same reason: a change that replaced the
        // hash and failed to revoke would leave a thief holding a live session behind a password its owner had
        // already changed.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        int changedRows = await ReplacePasswordHashAsync(userId, newPasswordHash, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (changedRows == 0)
        {
            // No such account, or it is soft-deleted behind the global query filter. Nothing to commit.
            return false;
        }

        await RevokeEveryRefreshTokenExceptAsync(userId, keepFamilyId, nowUtc, cancellationToken)
            .ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryIssuePasswordResetTokenAsync(
        string email,
        PasswordResetToken token,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        // The supersede and the insert must not be separable, for the reason SPEC-004 gives for
        // verification: a crash between them would leave an account with every link spent and no
        // replacement.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        // Unlike verification, an unverified account is eligible: someone who mistyped a password before
        // confirming their address must still be able to recover it.
        Guid userId = await _context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userId == Guid.Empty)
        {
            return false;
        }

        await SupersedeOutstandingResetTokensAsync(userId, nowUtc, cancellationToken).ConfigureAwait(false);

        token.UserId = userId;
        _context.PasswordResetTokens.Add(token);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<PasswordResetResult> TryResetPasswordAsync(
        string tokenHash,
        string newPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // All three writes or none — SPEC-007 escalation 1, option A. A reset that replaced the password
        // and failed to revoke the families would leave a thief holding a live session behind a password
        // its owner had already changed.
        await using IDbContextTransaction? transaction =
            await BeginIfNoneAsync(cancellationToken).ConfigureAwait(false);

        // One conditional UPDATE. Whoever affects the row won the token; everybody else is told so by the
        // database, under its own row lock, rather than by a read this code performed earlier.
        int consumedRows = await _context.PasswordResetTokens
            .Where(token => token.TokenHash == tokenHash
                && token.ConsumedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
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
            return new PasswordResetResult(
                await ClassifyLostResetRaceAsync(tokenHash, nowUtc, cancellationToken).ConfigureAwait(false),
                UserId: null);
        }

        Guid userId = await _context.PasswordResetTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => token.UserId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        await ReplacePasswordHashAsync(userId, newPasswordHash, nowUtc, cancellationToken).ConfigureAwait(false);
        await RevokeEveryRefreshTokenAsync(userId, nowUtc, cancellationToken).ConfigureAwait(false);
        await CommitIfOwnedAsync(transaction, cancellationToken).ConfigureAwait(false);

        // The id goes back so the audit record can name the account this reset acted on — SPEC-009.
        return new PasswordResetResult(ResetPasswordOutcome.Reset, userId);
    }

    /// <summary>
    /// Stamps every unspent reset token of an account, so exactly one reset link is live once the caller
    /// inserts the new one.
    /// </summary>
    private Task<int> SupersedeOutstandingResetTokensAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.PasswordResetTokens
            .Where(token => token.UserId == userId && token.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAtUtc, nowUtc)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>Writes the new hash. The old one is overwritten, never kept for comparison.</summary>
    private Task<int> ReplacePasswordHashAsync(
        Guid userId,
        string newPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.PasswordHash, newPasswordHash)
                    .SetProperty(user => user.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>
    /// Revokes every unrevoked refresh token of an account, which is what ends the sessions opened with
    /// the old password — `docs/api/ENDPOINTS.md` §1 and SDD §13.1.
    /// </summary>
    private Task<int> RevokeEveryRefreshTokenAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAtUtc, nowUtc)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>
    /// Revokes every unrevoked refresh token of an account **except** one family's — SPEC-028.
    /// </summary>
    /// <remarks>
    /// ASVS V7.4.3 asks that the other sessions end after a credential change. Ending the caller's own as well
    /// would log somebody out of the device they just used to change their password, which buys nothing: that
    /// session already proved it holds the new credential.
    /// </remarks>
    private Task<int> RevokeEveryRefreshTokenExceptAsync(
        Guid userId,
        Guid keepFamilyId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.RefreshTokens
            .Where(token => token.UserId == userId
                && token.FamilyId != keepFamilyId
                && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAtUtc, nowUtc)
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>
    /// Tells an expired reset token from one that is unknown or already spent. Only reached when the
    /// UPDATE affected nothing, so the happy path stays a single statement.
    /// </summary>
    private async Task<ResetPasswordOutcome> ClassifyLostResetRaceAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        PasswordResetToken? token = await _context.PasswordResetTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        // Absent and already consumed both answer Invalid: a caller holding a token that was spent must
        // not learn that it once existed. Only an unspent token past its window is Expired.
        return token is { ConsumedAtUtc: null } && token.ExpiresAtUtc <= nowUtc
            ? ResetPasswordOutcome.Expired
            : ResetPasswordOutcome.Invalid;
    }

    /// <summary>
    /// Stamps every unspent token of an account as consumed, so exactly one link is live once the
    /// caller inserts the new one — SPEC-004, owner decision 1.
    /// </summary>
    private Task<int> SupersedeOutstandingTokensAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.EmailVerificationTokens
            .Where(token => token.UserId == userId && token.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAtUtc, nowUtc)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the
                    // context would have stamped is stamped here — SDD §8.3.
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);

    /// <summary>
    /// Tells the three losing cases apart. Only reached when the UPDATE affected nothing, so the
    /// happy path stays a single statement.
    /// </summary>
    private async Task<VerifyEmailOutcome> ClassifyLostRaceAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        EmailVerificationToken? token = await _context.EmailVerificationTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        // Absent and already consumed both answer Invalid: a caller holding a token that was spent
        // must not learn that it once existed. Only an unspent token past its window is Expired.
        return token is { ConsumedAtUtc: null } && token.ExpiresAtUtc <= nowUtc
            ? VerifyEmailOutcome.Expired
            : VerifyEmailOutcome.Invalid;
    }

    /// <summary>
    /// Sets the flag on the account that owns the token, in the same transaction as the spend.
    /// Already verified is not an error: the update is idempotent on that column.
    /// </summary>
    private Task<int> MarkEmailVerifiedAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        _context.Users
            .Where(user => _context.EmailVerificationTokens
                .Any(token => token.TokenHash == tokenHash && token.UserId == user.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.IsEmailVerified, true)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the
                    // context would have stamped is stamped here — SDD §8.3.
                    .SetProperty(user => user.UpdatedAtUtc, nowUtc),
                cancellationToken);

    private static bool IsEmailAlreadyRegistered(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal)
        && string.Equals(postgresException.ConstraintName, "ux_users_email", StringComparison.Ordinal);

    /// <summary>
    /// Opens a transaction only when the request does not already have one.
    /// </summary>
    /// <remarks>
    /// SPEC-008 put the audit record inside the command's transaction, so a behaviour now opens one before
    /// the handler runs. EF Core refuses a second <c>BeginTransactionAsync</c> on the same context, so this
    /// method enlists instead of beginning, and returns null to say "not yours to commit".
    /// </remarks>
    private async Task<IDbContextTransaction?> BeginIfNoneAsync(CancellationToken cancellationToken) =>
        _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <summary>
    /// Commits only a transaction this repository opened. A null one belongs to the caller — today, the
    /// audit behaviour, which commits after the record is written.
    /// </summary>
    private static Task CommitIfOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null
            ? Task.CompletedTask
            : transaction.CommitAsync(cancellationToken);
}
