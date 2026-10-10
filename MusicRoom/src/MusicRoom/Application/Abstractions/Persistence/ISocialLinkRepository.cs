namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The provider identities bound to accounts — subject V.1, SPEC-041.
/// </summary>
/// <remarks>
/// Every read and write keys on the pair `(provider, providerUserId)`, never on an address. A provider can
/// change the address on an identity, and two providers can assert the same address; the pair is the only
/// thing that identifies a person at a provider — ADR-0008.
/// </remarks>
public interface ISocialLinkRepository
{
    /// <summary>Finds the account a provider identity signs in, if any.</summary>
    /// <param name="provider">The configuration row's name.</param>
    /// <param name="providerUserId">The `sub` of a validated ID token.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The account, or null when nothing is linked.</returns>
    Task<Guid?> FindUserIdAsync(string provider, string providerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Binds a provider identity to an account, if neither side is already taken.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="provider">The configuration row's name.</param>
    /// <param name="providerUserId">The `sub` of a validated ID token.</param>
    /// <param name="email">The address the provider asserted, stored as a label.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>What happened, so the endpoint can answer `201` or `409` without a read-then-write race.</returns>
    /// <remarks>
    /// The unique index on the pair is what decides, not a check in C#: two requests linking the same identity
    /// at the same moment both pass a read and only one passes the index — the same reasoning
    /// `ux_users_email` is relied on for.
    /// </remarks>
    Task<SocialLinkOutcome> TryLinkAsync(
        Guid userId,
        string provider,
        string providerUserId,
        string? email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an account from a provider identity, with its profile and its link, in one transaction.
    /// </summary>
    /// <param name="user">The account. Its password hash is null — SPEC-041, the owner's answer 2.</param>
    /// <param name="profile">The empty profile every account gets, as registration creates one.</param>
    /// <param name="link">The identity that will sign it in.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>True when the row was written; false when an index refused it.</returns>
    /// <remarks>
    /// Here rather than on <see cref="IUserRepository" /> for two reasons. It is the social path, and
    /// `UserRepository.cs` is at 295 code lines of S104's 300 — issue O27, which exists precisely so the next
    /// method lands somewhere else instead of raising a cap.
    /// <para>
    /// False covers both races: another request registered the same address, or another request linked the
    /// same provider identity. The caller retries the lookup rather than guessing which, because by then the
    /// winner's row is the answer to both questions.
    /// </para>
    /// </remarks>
    Task<bool> TryRegisterWithLinkAsync(
        Domain.Entities.User user,
        Domain.Entities.Profile profile,
        Domain.Entities.SocialLink link,
        CancellationToken cancellationToken);

    /// <summary>Everything linked to one account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>The links, oldest first.</returns>
    Task<IReadOnlyList<SocialLinkSummary>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Unbinds a provider identity, unless it is the only way into the account.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="provider">The configuration row's name.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// An account with no password and one link has exactly one way in. Removing it would lock the person out
    /// of their own account, so the repository refuses and the endpoint answers `409` — which is what
    /// `docs/api/ENDPOINTS.md` already publishes for this route.
    /// </remarks>
    Task<SocialUnlinkOutcome> TryUnlinkAsync(Guid userId, string provider, CancellationToken cancellationToken);
}

/// <summary>What a link attempt did.</summary>
public enum SocialLinkOutcome
{
    /// <summary>The identity is now bound to the account.</summary>
    Linked = 0,

    /// <summary>This identity already signs somebody in — possibly this same account.</summary>
    AlreadyLinked = 1,
}

/// <summary>What an unlink attempt did.</summary>
public enum SocialUnlinkOutcome
{
    /// <summary>The identity is no longer bound.</summary>
    Unlinked = 0,

    /// <summary>Nothing was bound for that provider.</summary>
    NotLinked = 1,

    /// <summary>It was the account's only way in, so it stays — see <see cref="ISocialLinkRepository"/>.</summary>
    LastSignInMethod = 2,
}

/// <summary>One link, as a person reading their own account sees it.</summary>
/// <param name="Provider">Which provider.</param>
/// <param name="Email">The address it asserted when the link was made, or null.</param>
/// <param name="LinkedAtUtc">When it was linked.</param>
public sealed record SocialLinkSummary(string Provider, string? Email, DateTime LinkedAtUtc);
