using MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;
using MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;
using MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;
using MusicRoom.Application.Features.Profiles.Queries.SearchUsers;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The read and write paths for a profile — subject V.1.
/// </summary>
/// <remarks>
/// Interfaces live here and implementations in <c>Persistence</c>, so <c>Application</c> never learns about
/// EF Core — ef-core-persistence.
/// </remarks>
public interface IProfileRepository
{
    /// <summary>Reads one account's own profile, preferences included.</summary>
    /// <param name="userId">The owner, from the validated token and never from the request.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The profile, or null when the account has none — which registration makes impossible for any
    /// account created after SPEC-011, and the backfill makes impossible for the ones before it.</returns>
    Task<OwnProfile?> FindOwnProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a subset of fields, each with its tier, and leaves every absent field untouched.
    /// </summary>
    /// <param name="userId">The owner.</param>
    /// <param name="changes">Only the fields the caller sent — this is a <c>PATCH</c>.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when the row was updated, false when the account has no profile row.</returns>
    Task<bool> TryUpdateProfileAsync(
        Guid userId,
        ProfileChanges changes,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an account's whole preference set, in one transaction.
    /// </summary>
    /// <param name="userId">The owner.</param>
    /// <param name="preferences">The new set. An empty set is valid and means "I named nothing".</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the set is replaced.</returns>
    /// <remarks>
    /// The contract's verb is <c>PUT</c>, so the set is replaced wholesale rather than merged: delete, then
    /// insert, inside the transaction the audit behaviour already owns.
    /// </remarks>
    Task ReplaceMusicPreferencesAsync(
        Guid userId,
        IReadOnlyList<MusicPreference> preferences,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one account's profile as a given relationship may see it — subject V.1's three tiers.
    /// </summary>
    /// <param name="userId">The account being read.</param>
    /// <param name="relationship">What the reader is to that account: self, friend or stranger. Resolved by the
    /// handler from the validated token and the friends graph, never from the request.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The projection, or null when no such account exists. **A field the relationship does not clear is not
    /// read at all** — the decision is made in the query, so a hidden value never exists in this process and
    /// cannot leak through a serialiser, a log line or a debugger (OWASP API3).
    /// </returns>
    Task<VisibleProfile?> FindVisibleProfileAsync(
        Guid userId,
        ProfileRelationship relationship,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds accounts whose display name starts with a prefix.
    /// </summary>
    /// <param name="displayNamePrefix">The prefix, already trimmed and known non-blank.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Matching accounts, id and display name only.</returns>
    /// <remarks>
    /// The display name only. Searching a profile field would let a stranger confirm a friends-only value by
    /// probing for it.
    /// </remarks>
    Task<IReadOnlyList<UserSearchResult>> SearchByDisplayNameAsync(
        string displayNamePrefix,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
