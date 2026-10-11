using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read path for the claims UserInfo may answer — SPEC-045.
/// </summary>
/// <remarks>
/// Its own file rather than a method on <see cref="UserRepository"/>, which is at S104's 300-line cap —
/// issue O27, and the same answer `PasswordContextRepository` got.
/// </remarks>
internal sealed class IdentityClaimsRepository : IIdentityClaimsReader
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public IdentityClaimsRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<IdentityClaims?> FindAsync(Guid userId, CancellationToken cancellationToken) =>

        // Projected in the database, so the password hash on that row is never read, never materialised and
        // never one logging mistake away from a response — SDD §13.5's rule applied to a new read path.
        _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new IdentityClaims(user.Email, user.IsEmailVerified, user.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
}
