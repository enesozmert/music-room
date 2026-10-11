using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// The write path for authorization codes — SPEC-044. Interfaces live here and implementations in
/// <c>Persistence</c>, so <c>Application</c> never learns about EF Core — ef-core-persistence.
/// </summary>
public interface IAuthorizationCodeRepository
{
    /// <summary>Writes a freshly issued code.</summary>
    /// <param name="code">The row to insert. Its digest is computed by the caller; the plaintext never
    /// reaches this layer, so no implementation can log it.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task IssueAsync(AuthorizationCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Spends a code once and reports what it was bound to.
    /// </summary>
    /// <param name="codeHash">The digest of the presented code.</param>
    /// <param name="clientId">The client presenting it. Part of the predicate, not checked afterwards.</param>
    /// <param name="redirectUri">The redirect URI presented, which must be the one the code was issued
    /// against — RFC 6749 §4.1.3.</param>
    /// <param name="nowUtc">The instant expiry is judged against, and the <c>consumed_at_utc</c> stamp.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The binding on success, and <see cref="AuthorizationCodeRedemption.None"/> for every failure —
    /// unknown, expired, already spent, or issued to another client or another redirect URI. Single use is
    /// arbitrated by the <c>consumed_at_utc IS NULL</c> predicate in the database, not by a read-then-write
    /// check here: the same reasoning <see cref="IRefreshTokenRepository.TryRotateAsync"/> records.
    /// </returns>
    Task<AuthorizationCodeRedemption> TryConsumeAsync(
        string codeHash,
        string clientId,
        string redirectUri,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// What a spent code was bound to.
/// </summary>
/// <param name="Spent">True when this caller won the code. False for every refusal.</param>
/// <param name="UserId">The account that authorised the client.</param>
/// <param name="CodeChallenge">The PKCE challenge the verifier is checked against, by the caller: the
/// comparison is a security decision and belongs in the handler, not in a repository.</param>
/// <param name="Nonce">The nonce the client sent, to echo into the ID token, or null.</param>
/// <param name="AuthenticatedAtUtc">When the account proved itself — the ID token's <c>auth_time</c>.</param>
/// <param name="Scope">The scopes the code was issued for.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as this method's return value,
/// and SPEC-044's Files table is the contract for what this change may create.
/// </remarks>
public sealed record AuthorizationCodeRedemption(
    bool Spent,
    Guid UserId,
    string CodeChallenge,
    string? Nonce,
    DateTime AuthenticatedAtUtc,
    string Scope)
{
    /// <summary>Every refusal, which is deliberately one answer — the endpoint says `invalid_grant`.</summary>
    public static AuthorizationCodeRedemption None { get; } =
        new(false, Guid.Empty, string.Empty, null, default, string.Empty);
}
