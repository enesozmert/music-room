namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The short-lived codes an owner shows at the venue — ADR-0014's second presence proof.
/// </summary>
/// <remarks>
/// Only the digest is stored, and spending is arbitrated by a unique index on the redemption rather than by a
/// read: two parallel votes carrying the same code cannot both spend it — SPEC-018 decision 4.
/// </remarks>
public interface IEventCodeRepository
{
    /// <summary>Stores a new code's digest with its expiry.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="codeHash">The digest of the code the owner was shown.</param>
    /// <param name="expiresAtUtc">When it stops being accepted.</param>
    /// <param name="issuedByUserId">The owner issuing it.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task IssueAsync(
        Guid eventId,
        string codeHash,
        DateTime expiresAtUtc,
        Guid issuedByUserId,
        CancellationToken cancellationToken);

    /// <summary>Spends a code for one account, once.</summary>
    /// <param name="eventId">The event the code must belong to.</param>
    /// <param name="codeHash">The digest of the submitted code.</param>
    /// <param name="userId">Who is spending it.</param>
    /// <param name="nowUtc">Server time, which decides expiry.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when the code was valid, unexpired and unspent by this account, and has now been spent by them.
    /// False for every other case — unknown, expired, another event's, or already spent — because telling a
    /// caller which one it was tells them whether a code exists.
    /// </returns>
    Task<bool> TrySpendAsync(
        Guid eventId,
        string codeHash,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
