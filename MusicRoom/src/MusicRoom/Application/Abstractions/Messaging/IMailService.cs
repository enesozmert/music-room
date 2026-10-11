namespace MusicRoom.Application.Abstractions.Messaging;

/// <summary>
/// Outbound transactional mail. The interface is declared here and implemented in
/// <c>Infrastructure</c>, so this layer never learns which provider delivers the message —
/// ADR-0013, ADR-0028.
/// </summary>
/// <remarks>
/// The interface names no provider, template id or transport. Swapping the Development sender for the
/// hosted provider is one registration, not a change here.
/// <para>
/// **Each message is one record** — SPEC-052. They were parameter lists until the messages gained a
/// language, a greeting and, for verification, a code beside the token; five parameters is the limit the
/// root .editorconfig enforces, and a message is one thing anyway.
/// </para>
/// </remarks>
public interface IMailService
{
    /// <summary>
    /// Delivers the verification link **and** the code for a freshly registered address — SDD §10.1.
    /// </summary>
    /// <param name="message">Who it goes to, in which language, and the two credentials.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendEmailVerificationAsync(VerificationMail message, CancellationToken cancellationToken);

    /// <summary>
    /// Delivers the password reset link — SDD §13.1. No code: see <see cref="PasswordResetMail" />.
    /// </summary>
    /// <param name="message">Who it goes to, in which language, and the token.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendPasswordResetAsync(PasswordResetMail message, CancellationToken cancellationToken);

    /// <summary>
    /// Delivers the one welcome message an account ever receives — SPEC-052 decision 9.
    /// </summary>
    /// <param name="message">Who it goes to, and in which language.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendWelcomeAsync(WelcomeMail message, CancellationToken cancellationToken);
}

/// <summary>
/// The verification message — a link and a code, which are two credentials for one record.
/// </summary>
/// <param name="EmailAddress">The address that registered.</param>
/// <param name="DisplayName">What to call the reader. Never a credential, and never an account id.</param>
/// <param name="Token">The <em>plaintext</em> 256-bit token the link carries. The sender is the only
/// component that may ever see it: the database holds its digest.</param>
/// <param name="Code">The <em>plaintext</em> six-character code, for a reader who would rather type than
/// click. Its digest is stored beside the token's, and five wrong attempts spend the row.</param>
/// <param name="Language">Which language to write in — the one the request asked for.</param>
public sealed record VerificationMail(
    string EmailAddress,
    string DisplayName,
    string Token,
    string Code,
    MailLanguage Language);

/// <summary>
/// The password reset message.
/// </summary>
/// <param name="EmailAddress">The address that asked for a reset.</param>
/// <param name="DisplayName">What to call the reader.</param>
/// <param name="Token">The <em>plaintext</em> reset token. A stronger credential than the verification
/// token: whoever holds it can replace the account's password.</param>
/// <param name="Language">Which language to write in.</param>
/// <remarks>
/// **No code here, deliberately** — SPEC-052. A six-character code that replaces a password is a weak
/// credential for the strongest action in the system. Proving an address is what a code is for.
/// </remarks>
public sealed record PasswordResetMail(
    string EmailAddress,
    string DisplayName,
    string Token,
    MailLanguage Language);

/// <summary>
/// The welcome message, which carries no credential at all.
/// </summary>
/// <param name="EmailAddress">The address that just signed in for the first time.</param>
/// <param name="DisplayName">What to call the reader.</param>
/// <param name="Language">Which language to write in.</param>
public sealed record WelcomeMail(string EmailAddress, string DisplayName, MailLanguage Language);

/// <summary>
/// The languages this product writes mail in — SPEC-052 decision 6.
/// </summary>
/// <remarks>
/// Two, because two are written. A language with no translation behind it would be a promise, and the
/// request's <c>Accept-Language</c> picks between them with English as the default.
/// </remarks>
public enum MailLanguage
{
    /// <summary>English, and what any unrecognised request gets.</summary>
    English = 0,

    /// <summary>Turkish.</summary>
    Turkish = 1,
}
