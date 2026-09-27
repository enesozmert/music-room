using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core write path for refresh tokens.
/// </summary>
internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a
    /// public constructor only, and an internal one leaves
    /// <see cref="IRefreshTokenRepository"/> unresolvable.
    /// </remarks>
    public RefreshTokenRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task IssueAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        _context.RefreshTokens.Add(token);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenRotation> TryRotateAsync(
        string tokenHash,
        RefreshToken replacement,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        // The spend and the insert must not be separable. A crash between them would leave a session
        // whose token is spent and whose replacement does not exist, and the client would hold a
        // value no request can ever exchange.
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // One conditional UPDATE. Whoever affects the row won the exchange; everybody else is told so
        // by the database, under its own row lock, rather than by a read this code performed earlier.
        int consumedRows = await _context.RefreshTokens
            .Where(token => token.TokenHash == tokenHash
                && token.ConsumedAtUtc == null
                && token.RevokedAtUtc == null
                && token.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.ConsumedAtUtc, nowUtc)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the
                    // context would have stamped is stamped here — SDD §8.3.
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);

        if (consumedRows == 0)
        {
            return await ClassifyLostRaceAsync(tokenHash, nowUtc, transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        RefreshTokenRotation rotated = await InsertReplacementAsync(
                tokenHash,
                replacement,
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return rotated;
    }

    /// <summary>
    /// Copies the family's identity and its absolute expiry onto the replacement, then writes it.
    /// </summary>
    /// <remarks>
    /// The expiry is inherited rather than recalculated — the owner's absolute-lifetime decision of
    /// 2026-09-27. A replacement that started its own 30 days would let a stolen family be kept alive
    /// for ever by rotating it.
    /// </remarks>
    private async Task<RefreshTokenRotation> InsertReplacementAsync(
        string tokenHash,
        RefreshToken replacement,
        CancellationToken cancellationToken)
    {
        RefreshToken spent = await _context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(token => token.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        replacement.UserId = spent.UserId;
        replacement.FamilyId = spent.FamilyId;
        replacement.ExpiresAtUtc = spent.ExpiresAtUtc;

        _context.RefreshTokens.Add(replacement);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshTokenRotation(RefreshTokenRotationStatus.Rotated, spent.UserId);
    }

    /// <summary>
    /// Tells reuse apart from the cases that are merely refused. Only reached when the UPDATE affected
    /// nothing, so the happy path stays a single statement.
    /// </summary>
    /// <remarks>
    /// An already-spent token means the value leaked: the holder of a rotated token is either the
    /// client replaying it or whoever copied it, and neither can be told from the other, so the whole
    /// family is revoked — SDD §13.1. Unknown, expired and already-revoked are refused without
    /// touching anything, and the endpoint answers all four identically.
    /// </remarks>
    private async Task<RefreshTokenRotation> ClassifyLostRaceAsync(
        string tokenHash,
        DateTime nowUtc,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        RefreshToken? token = await _context.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (token is null || token.ConsumedAtUtc is null)
        {
            return new RefreshTokenRotation(RefreshTokenRotationStatus.Rejected, Guid.Empty);
        }

        await RevokeFamilyAsync(token.FamilyId, nowUtc, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshTokenRotation(RefreshTokenRotationStatus.Reused, Guid.Empty);
    }

    /// <summary>
    /// Stamps every unrevoked row of a family, so no token descended from the leaked login is usable.
    /// </summary>
    private Task<int> RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken cancellationToken) =>
        _context.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAtUtc, nowUtc)
                    // ExecuteUpdateAsync does not run through SaveChangesAsync, so the column the
                    // context would have stamped is stamped here — SDD §8.3.
                    .SetProperty(token => token.UpdatedAtUtc, nowUtc),
                cancellationToken);
}
