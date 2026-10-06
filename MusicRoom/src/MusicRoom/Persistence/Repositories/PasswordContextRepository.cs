using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The one read the context screen needs on the reset path — SPEC-035.
/// </summary>
/// <remarks>
/// A file of its own rather than two more methods on <see cref="UserRepository"/>, which is at 293 code lines of
/// the 300 S104 enforces (issue O27). The owner's answer of 2026-10-06 is explicit about which way that goes: a
/// new file, never a raised cap — the same wall SPEC-032 hit in `Program.cs`.
/// </remarks>
internal sealed class PasswordContextRepository : IPasswordContextReader
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public PasswordContextRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// A join rather than a navigation, because <c>PasswordResetToken</c> carries a <c>UserId</c> and no
    /// reference. Read-only and unfiltered by expiry: this answers which words to screen, never which status
    /// code to send.
    /// </remarks>
    public async Task<PasswordContextWords?> FindByPasswordResetTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        await _context.PasswordResetTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Join(
                _context.Users.AsNoTracking(),
                token => token.UserId,
                user => user.Id,
                (token, user) => new PasswordContextWords(user.Email, user.DisplayName))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
