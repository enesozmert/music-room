using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
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

    private static bool IsEmailAlreadyRegistered(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal)
        && string.Equals(postgresException.ConstraintName, "ux_users_email", StringComparison.Ordinal);
}
