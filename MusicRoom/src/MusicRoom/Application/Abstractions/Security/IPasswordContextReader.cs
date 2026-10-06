namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// The account words a password must not contain, for a path whose command does not carry them — ADR-0042
/// as corrected by SPEC-035.
/// </summary>
/// <remarks>
/// The reset path chooses a password and knows nothing about the account: its command carries a token and a new
/// password, and <c>ResetPasswordCommandHandler</c> read **nothing** before SPEC-035 — `TryResetPasswordAsync`
/// spends the token and replaces the hash in one transaction. The access token carries no address and no display
/// name either, by design. So the context words come from one read, taken **before** the write, which is what
/// keeps a refused password from costing the caller their reset link.
/// <para>
/// Separate from <see cref="MusicRoom.Application.Abstractions.Persistence.IUserRepository"/> on purpose:
/// its implementation is at 293 code lines of the 300 the root `.editorconfig` enforces (S104, issue O27), and
/// the answer to a full file is another file rather than a raised cap — SPEC-032's precedent, owner's answer 2
/// of 2026-10-06.
/// </para>
/// </remarks>
public interface IPasswordContextReader
{
    /// <summary>Finds the context words for the account a reset token belongs to.</summary>
    /// <param name="tokenHash">The digest of the submitted token. The caller hashes it, so no implementation of
    /// this interface can log the token.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The words, or <see langword="null"/> when no token has that digest.</returns>
    /// <remarks>
    /// Unfiltered by expiry and by consumption, deliberately: this read decides which words to screen against
    /// and never decides the response. A spent or expired token is classified by the write path exactly as it
    /// was before, so a replayed link still answers `400`.
    /// </remarks>
    Task<PasswordContextWords?> FindByPasswordResetTokenAsync(string tokenHash, CancellationToken cancellationToken);
}

/// <summary>
/// The two account values a chosen password must not contain.
/// </summary>
/// <param name="Email">The address. Only its local part is screened — the domain is shared by every caller on
/// it, and refusing `example.com` would refuse a word the account's owner did not choose.</param>
/// <param name="DisplayName">The name other users see.</param>
/// <remarks>
/// Declared beside the interface rather than in its own file: it exists only as that method's return value, which
/// is how <c>UserCredential</c> and <c>PasswordResetResult</c> are declared beside theirs.
/// </remarks>
public sealed record PasswordContextWords(string Email, string DisplayName);
