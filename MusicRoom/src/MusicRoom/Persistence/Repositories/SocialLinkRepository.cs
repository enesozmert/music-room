using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Npgsql;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core path for provider identities — SPEC-041.
/// </summary>
/// <remarks>
/// Reached through <c>Set&lt;SocialLink&gt;()</c> rather than a <c>DbSet</c> property, because one more
/// property crosses S1200's coupling cap on the context and the mapping is discovered from the assembly
/// either way — the note in <see cref="MusicRoomDbContext"/> says so at the other end.
/// </remarks>
internal sealed class SocialLinkRepository : ISocialLinkRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type, as the other repositories are: the container activates a registered
    /// implementation through a public constructor only.
    /// </remarks>
    public SocialLinkRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Guid?> FindUserIdAsync(
        string provider,
        string providerUserId,
        CancellationToken cancellationToken) =>
        _context.Set<SocialLink>()
            .AsNoTracking()
            .Where(link => link.Provider == provider && link.ProviderUserId == providerUserId)
            .Select(link => (Guid?)link.UserId)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<SocialLinkOutcome> TryLinkAsync(
        Guid userId,
        string provider,
        string providerUserId,
        string? email,
        CancellationToken cancellationToken)
    {
        SocialLink link = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            ProviderUserId = providerUserId,
            Email = email,
        };

        _context.Set<SocialLink>().Add(link);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return SocialLinkOutcome.Linked;
        }
        catch (DbUpdateException exception) when (IsAlreadyLinked(exception))
        {
            // The database arbitrated the race and refused this writer — either the identity already signs
            // somebody in, or this account already has a link for that provider. Detach, so the context is
            // not left holding a row it failed to write.
            _context.Entry(link).State = EntityState.Detached;

            return SocialLinkOutcome.AlreadyLinked;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRegisterWithLinkAsync(
        User user,
        Profile profile,
        SocialLink link,
        CancellationToken cancellationToken)
    {
        _context.Users.Add(user);
        _context.Profiles.Add(profile);
        _context.Set<SocialLink>().Add(link);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException exception) when (IsAlreadyLinked(exception) || IsAddressTaken(exception))
        {
            // One of the three indexes arbitrated a race. Detach all three entries so the context is not
            // left holding rows it failed to write — the same shape `TryRegisterAsync` uses.
            _context.Entry(user).State = EntityState.Detached;
            _context.Entry(profile).State = EntityState.Detached;
            _context.Entry(link).State = EntityState.Detached;

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SocialLinkSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await _context.Set<SocialLink>()
            .AsNoTracking()
            .Where(link => link.UserId == userId)
            .OrderBy(link => link.CreatedAtUtc)
            .Select(link => new SocialLinkSummary(link.Provider, link.Email, link.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<SocialUnlinkOutcome> TryUnlinkAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken)
    {
        SocialLink? link = await _context.Set<SocialLink>()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.Provider == provider,
                cancellationToken)
            .ConfigureAwait(false);

        if (link is null)
        {
            return SocialUnlinkOutcome.NotLinked;
        }

        // An account with no password and one link has exactly one way in. Counted here rather than
        // guessed: the password may have been set after the link was made.
        bool hasPassword = await _context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.PasswordHash != null, cancellationToken)
            .ConfigureAwait(false);

        int links = await _context.Set<SocialLink>()
            .CountAsync(candidate => candidate.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!hasPassword && links <= 1)
        {
            return SocialUnlinkOutcome.LastSignInMethod;
        }

        _context.Set<SocialLink>().Remove(link);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SocialUnlinkOutcome.Unlinked;
    }

    /// <summary>Whether the write failed because the address is already registered.</summary>
    private static bool IsAddressTaken(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_users_email",
        };

    /// <summary>Whether the write failed because one of the two unique indexes refused it.</summary>
    private static bool IsAlreadyLinked(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation
        && (violation.ConstraintName is "ux_social_links_provider_provider_user_id"
            or "ux_social_links_user_id_provider");
}
