using MusicRoom.Application.Features.Auth.Queries.ListSessions;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The write path for refresh tokens. Interfaces live here and implementations in
/// <c>Persistence</c>, so <c>Application</c> never learns about EF Core — ef-core-persistence.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Writes the first token of a new family, which is what a successful login is.</summary>
    /// <param name="token">The row to insert. Its digest is computed by the caller; the plaintext
    /// never reaches this layer, so no implementation can log it.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task IssueAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Spends a refresh token and inserts its replacement, in one transaction.
    /// </summary>
    /// <param name="tokenHash">The digest of the presented token.</param>
    /// <param name="replacement">The row to insert when the exchange succeeds. Its
    /// <see cref="RefreshToken.UserId"/>, <see cref="RefreshToken.FamilyId"/> and
    /// <see cref="RefreshToken.ExpiresAtUtc"/> are assigned by the implementation, because the family
    /// and its absolute expiry are read inside the same transaction that spends the old row.</param>
    /// <param name="nowUtc">The instant the request is judged against, used for the expiry
    /// comparison and for every stamp this method writes.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The outcome, and on success the account the session belongs to. Single use is arbitrated by
    /// the <c>consumed_at_utc IS NULL AND revoked_at_utc IS NULL</c> predicate in the database, not
    /// by a read-then-write check here — the same reasoning as <c>ux_users_email</c>.
    /// </returns>
    Task<RefreshTokenRotation> TryRotateAsync(
        string tokenHash,
        RefreshToken replacement,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one of an account's refresh-token families — logout, SPEC-010.
    /// </summary>
    /// <param name="userId">The account whose session it must be.</param>
    /// <param name="familyId">The family to revoke, from the token's <c>sid</c> or from the route.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when a live family of that account was revoked. False when there was none — an unknown id, one
    /// already revoked, or **one belonging to somebody else**. The three are deliberately one answer: the
    /// endpoint replies <c>404</c> to all of them, because a distinct answer would confirm that another
    /// account's session exists (`docs/api/ENDPOINTS.md`, "a resource the caller may not see returns 404").
    /// </returns>
    Task<bool> TryRevokeFamilyAsync(
        Guid userId,
        Guid familyId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the family a presented refresh token belongs to — <c>POST /oauth2/revoke</c>, SPEC-045.
    /// </summary>
    /// <param name="tokenHash">The digest of the presented token. The caller hashes it; the plaintext never
    /// reaches this layer.</param>
    /// <param name="nowUtc">The instant the rows are stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// True when a family was revoked, false when the token is unknown or its family was already revoked.
    /// **The endpoint answers `200` either way** — RFC 7009 §2.2 — so this value exists for the audit record
    /// and for nothing the caller can see.
    /// </returns>
    /// <remarks>
    /// No account is named, unlike <see cref="TryRevokeFamilyAsync"/>: this route is anonymous and the token
    /// itself is the credential. Whoever holds a live refresh token may end its session, which is what a
    /// client doing so at sign-out is doing.
    /// <para>
    /// The whole family, not the one row: a session is a family (ADR-0032), and revoking a single row would
    /// leave its replacement live — a revocation that does not revoke.
    /// </para>
    /// </remarks>
    Task<bool> TryRevokeByTokenAsync(
        string tokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the scopes granted when a session was opened — <c>GET /oauth2/userinfo</c>, SPEC-045.
    /// </summary>
    /// <param name="userId">The account from the validated token's <c>sub</c>.</param>
    /// <param name="familyId">The session from the validated token's <c>sid</c>.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// What the session was granted. <see cref="GrantedScope.Found"/> is false only when no row of that
    /// family belongs to that account, which a validated token makes impossible today; UserInfo answers such
    /// a caller the minimal claim set rather than guessing.
    /// </returns>
    /// <remarks>
    /// The account is part of the lookup rather than checked after it, so another account's session cannot be
    /// read by naming its id — the same shape <see cref="TryRevokeFamilyAsync"/> uses.
    /// </remarks>
    Task<GrantedScope> FindGrantedScopeAsync(
        Guid userId,
        Guid familyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists an account's live sessions, newest first.
    /// </summary>
    /// <param name="userId">The account whose sessions to list. Never widened: this is the <c>self</c> gate.</param>
    /// <param name="nowUtc">The instant expiry is judged against.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>One entry per live family, carrying no token and no digest (§13.5).</returns>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// What an attempted exchange did.
/// </summary>
/// <param name="Status">Rotated, reused or rejected.</param>
/// <param name="FamilyId">The family the rotated token belongs to, so the replacement's access token can
/// carry the same <c>sid</c> — a rotation continues the same session (SPEC-010).</param>
/// <param name="UserId">The account the session belongs to. Set on
/// <see cref="RefreshTokenRotationStatus.Rotated"/> and on
/// <see cref="RefreshTokenRotationStatus.Reused"/> — a reuse proves the presented value was once issued to
/// that account, so the audit row may name it. <see cref="Guid.Empty"/> on
/// <see cref="RefreshTokenRotationStatus.Rejected"/>, where nothing was proved.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as this method's return
/// value, and SPEC-005's Files table is the contract for what this change may create.
/// </remarks>
public sealed record RefreshTokenRotation(RefreshTokenRotationStatus Status, Guid UserId, Guid FamilyId);

/// <summary>
/// What a session was granted — SPEC-045.
/// </summary>
/// <param name="Found">Whether a row of that family belongs to that account.</param>
/// <param name="Scope">The scopes granted when the family was opened, or null for a first-party session.
/// Null is not "nothing granted": it is a session from the credential endpoints, where nobody asked.</param>
/// <remarks>
/// A record rather than a bare <c>string?</c>, because null already means something here: without the
/// <see cref="Found"/> flag, "no such session" and "a session with no granted scope" would be one value with
/// two opposite meanings.
/// </remarks>
public sealed record GrantedScope(bool Found, string? Scope)
{
    /// <summary>No row of that family belongs to that account.</summary>
    public static GrantedScope None { get; } = new(false, null);
}

/// <summary>
/// The three ways an exchange ends.
/// </summary>
public enum RefreshTokenRotationStatus
{
    /// <summary>The presented token was live: it is now spent and a replacement exists.</summary>
    Rotated = 0,

    /// <summary>
    /// The presented token had already been exchanged. That means the value leaked, so the whole
    /// family is revoked — SDD §13.1 theft detection.
    /// </summary>
    Reused = 1,

    /// <summary>
    /// Unknown, expired or already revoked. The three are deliberately one outcome: the endpoint
    /// answers a bodiless <c>401</c> to all of them, so a caller learns nothing about which tokens
    /// exist.
    /// </summary>
    Rejected = 2,
}
