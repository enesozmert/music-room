using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Npgsql;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core write path for accounts.
/// </summary>
internal sealed class UserRepository : IUserRepository
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
        CancellationToken cancellationToken)
    {
        _context.Users.Add(user);
        _context.EmailVerificationTokens.Add(verificationToken);

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
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return VerifyEmailOutcome.Verified;
    }

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
}
