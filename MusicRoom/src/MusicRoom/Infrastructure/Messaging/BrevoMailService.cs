using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// Sends the credential messages through Brevo's transactional API — ADR-0013, SPEC-024.
/// </summary>
/// <remarks>
/// One `POST` to `/v3/smtp/email` per message, authenticated by the `api-key` header. The base address, the
/// timeout and the key live on the typed client that <see cref="MailServiceRegistration"/> configures, so this
/// class holds no secret of its own and nothing here can log one.
/// <para>
/// **One attempt, no retry** — SPEC-024 decision 4. The resend endpoint is the retry path and it is
/// rate-limited; a retry inside this class would multiply quota spend (T-19) after the limiter has already let
/// the request through. A failure throws, and <c>CredentialMailDispatcher</c> swallows it so that a dead
/// provider costs a log line rather than turning an existing account into a `500` — which is what keeps the
/// `202` from being an enumeration oracle.
/// </para>
/// <para>
/// **The provider's response is untrusted input** — API10 in <c>docs/owasp/API-TOP10-CHECKLIST.md</c>. Only the
/// status code and `messageId` are read. The body is never echoed to a caller, never logged whole, and never
/// put into an exception message.
/// </para>
/// </remarks>
internal sealed class BrevoMailService : IMailService
{
    /// <summary>The transactional send path, relative to the client's base address.</summary>
    internal const string SendPath = "v3/smtp/email";

    private readonly HttpClient _client;
    private readonly ILogger<BrevoMailService> _logger;
    private readonly MailOptions _options;

    /// <summary>Takes the configured client, a logger and the sender's own settings.</summary>
    /// <param name="client">The typed client, carrying the base address, the timeout and the API key.</param>
    /// <param name="logger">The application logger.</param>
    /// <param name="options">The sender address and the two link bases.</param>
    /// <remarks>
    /// Public on an internal type: the container activates the registered implementation through a public
    /// constructor only.
    /// </remarks>
    public BrevoMailService(HttpClient client, ILogger<BrevoMailService> logger, MailOptions options)
    {
        _client = client;
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc />
    public Task SendEmailVerificationAsync(VerificationMail message, CancellationToken cancellationToken) =>
        SendAsync(
            message?.EmailAddress ?? throw new ArgumentNullException(nameof(message)),
            CredentialMailTemplates.Verification(message, _options.VerificationLinkBaseUrl),
            cancellationToken);

    /// <inheritdoc />
    public Task SendPasswordResetAsync(PasswordResetMail message, CancellationToken cancellationToken) =>
        SendAsync(
            message?.EmailAddress ?? throw new ArgumentNullException(nameof(message)),
            CredentialMailTemplates.Reset(message, _options.PasswordResetLinkBaseUrl),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The welcome message points at the verification screen's base, which is the one URL this service
    /// knows that belongs to the product rather than to a credential. With none configured the message
    /// still says what the product is for and carries no button — SPEC-052.
    /// </remarks>
    public Task SendWelcomeAsync(WelcomeMail message, CancellationToken cancellationToken) =>
        SendAsync(
            message?.EmailAddress ?? throw new ArgumentNullException(nameof(message)),
            CredentialMailTemplates.Welcome(message, _options.VerificationLinkBaseUrl),
            cancellationToken);

    private async Task SendAsync(
        string emailAddress,
        RenderedMail mail,
        CancellationToken cancellationToken)
    {
        BrevoSendRequest request = new(
            new BrevoSender(_options.FromAddress ?? string.Empty, _options.FromName),
            [new BrevoRecipient(emailAddress)],
            mail.Subject,
            mail.Html,
            mail.Text);

        using HttpResponseMessage response = await _client
            .PostAsJsonAsync(SendPath, request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The status code and nothing else. A provider error document may contain anything, including
            // text that echoes our own request, so it is not logged and not put into this message — API10.
            throw new HttpRequestException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Brevo refused the message with status {(int)response.StatusCode}."),
                inner: null,
                statusCode: response.StatusCode);
        }

        BrevoSendResponse? accepted = await response.Content
            .ReadFromJsonAsync<BrevoSendResponse>(cancellationToken)
            .ConfigureAwait(false);

        // The recipient is logged and the token is not: the address is already in `audit_records` for this
        // request, and the token is the credential — `secrets-management`, and the never-logged list in
        // `application-security`.
        _logger.LogInformation(
            "Credential mail accepted by the provider for {EmailAddress}, message {MailMessageId}.",
            emailAddress,
            accepted?.MessageId);
    }
}

/// <summary>
/// Brevo's transactional send request, as the provider's API documents it.
/// </summary>
/// <param name="Sender">The from address and optional display name.</param>
/// <param name="To">The recipients. One, always: these are credential messages.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="HtmlContent">The HTML body.</param>
/// <param name="TextContent">The plain-text body.</param>
/// <remarks>
/// Declared here rather than in its own file: these three records exist only as this sender's wire shape, and
/// SPEC-024's Files table is the contract for what this change may create. The property names are the
/// provider's, which is why they carry explicit <c>JsonPropertyName</c> attributes instead of relying on a
/// naming policy that the rest of this application sets for its own contract.
/// </remarks>
internal sealed record BrevoSendRequest(
    [property: JsonPropertyName("sender")] BrevoSender Sender,
    [property: JsonPropertyName("to")] IReadOnlyList<BrevoRecipient> To,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("htmlContent")] string HtmlContent,
    [property: JsonPropertyName("textContent")] string TextContent);

/// <summary>The sender Brevo shows on the message.</summary>
/// <param name="Email">The from address — verified, or Brevo's sandbox sender.</param>
/// <param name="Name">The display name, or null.</param>
internal sealed record BrevoSender(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>One recipient.</summary>
/// <param name="Email">The address that registered or asked for a reset.</param>
internal sealed record BrevoRecipient([property: JsonPropertyName("email")] string Email);

/// <summary>The only part of the provider's answer this sender reads.</summary>
/// <param name="MessageId">The provider's id for the accepted message, for correlating a delivery
/// question later. Null when the provider answers without one.</param>
internal sealed record BrevoSendResponse([property: JsonPropertyName("messageId")] string? MessageId);
