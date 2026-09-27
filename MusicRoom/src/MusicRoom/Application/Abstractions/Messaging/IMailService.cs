namespace MusicRoom.Application.Abstractions.Messaging;

/// <summary>
/// Outbound transactional mail. The interface is declared here and implemented in
/// <c>Infrastructure</c>, so this layer never learns which provider delivers the message —
/// ADR-0013, ADR-0028.
/// </summary>
/// <remarks>
/// The interface names no provider, template id or transport. Swapping the Development sender for
/// the hosted provider of slice S1a is therefore one registration, not a change here.
/// </remarks>
public interface IMailService
{
    /// <summary>
    /// Delivers the verification link for a freshly registered address — SDD §10.1 step 4.
    /// </summary>
    /// <param name="emailAddress">The address that registered.</param>
    /// <param name="token">The <em>plaintext</em> verification token. The sender is the only
    /// component that may ever see it: the database holds its SHA-256 digest, and no other layer
    /// keeps a copy.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendEmailVerificationAsync(
        string emailAddress,
        string token,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delivers the password reset link — SDD §13.1.
    /// </summary>
    /// <param name="emailAddress">The address that asked for a reset.</param>
    /// <param name="token">The <em>plaintext</em> reset token. It is a stronger credential than the
    /// verification token: whoever holds it can replace the account's password. The sender is the only
    /// component that may ever see it, the database holds its SHA-256 digest, and no other layer keeps a
    /// copy.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendPasswordResetAsync(
        string emailAddress,
        string token,
        CancellationToken cancellationToken);
}
