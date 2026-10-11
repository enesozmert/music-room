using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core write path for authorization codes — SPEC-044.
/// </summary>
/// <remarks>
/// Reached through <c>Set&lt;AuthorizationCode&gt;()</c> rather than a property on the context, for the
/// reason <c>SocialLinkRepository</c> records: S1200 caps the context's coupling, and a property is sugar.
/// </remarks>
internal sealed class AuthorizationCodeRepository : IAuthorizationCodeRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public AuthorizationCodeRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task IssueAsync(AuthorizationCode code, CancellationToken cancellationToken)
    {
        _context.Set<AuthorizationCode>().Add(code);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuthorizationCodeRedemption> TryConsumeAsync(
        string codeHash,
        string clientId,
        string redirectUri,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // One conditional UPDATE, and every part of the binding is in its predicate. Whoever affects the row
        // won the exchange; everybody else is told so by the database under its own row lock, rather than by
        // a read this code performed earlier — the same posture as the refresh-token rotation.
        int consumedRows = await _context.Set<AuthorizationCode>()
            .Where(code => code.CodeHash == codeHash
                && code.ClientId == clientId
                && code.RedirectUri == redirectUri
                && code.ConsumedAtUtc == null
                && code.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(code => code.ConsumedAtUtc, nowUtc)

                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the context
                    // would have stamped is stamped here — SDD §8.3.
                    .SetProperty(code => code.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (consumedRows == 0)
        {
            // Unknown, expired, already spent, or bound to something else. One answer, because the endpoint
            // says `invalid_grant` to all four and a caller that could tell them apart would have an oracle.
            return AuthorizationCodeRedemption.None;
        }

        AuthorizationCode spent = await _context.Set<AuthorizationCode>()
            .AsNoTracking()
            .SingleAsync(code => code.CodeHash == codeHash, cancellationToken)
            .ConfigureAwait(false);

        return new AuthorizationCodeRedemption(
            true,
            spent.UserId,
            spent.CodeChallenge,
            spent.Nonce,
            spent.AuthenticatedAtUtc,
            spent.Scope);
    }
}
