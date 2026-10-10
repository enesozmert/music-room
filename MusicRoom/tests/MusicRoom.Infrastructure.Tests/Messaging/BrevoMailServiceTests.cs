using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Messaging;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Messaging;

/// <summary>
/// The hosted sender: what it puts on the wire, what it logs, and what it does when the provider refuses —
/// ADR-0013, SPEC-024.
/// </summary>
/// <remarks>
/// The sender is resolved from the registration that is the only way it is ever created, as
/// <see cref="LoggingMailServiceTests"/> does, because the type is internal and the registration is what the
/// composition root actually uses. The transport is a stub: no test spends provider quota (T-19).
/// </remarks>
public sealed class BrevoMailServiceTests
{
    internal const string ApiKey = "a-key-that-stands-in-for-the-real-one";
    internal const string FromAddress = "sandbox@loadtest.invalid";
    internal const string FromName = "Music Room";
    internal const string EmailAddress = "player@example.com";
    internal const string Token = "a-token-that-stands-in-for-43-base64url-chars";
    internal const string VerifyBase = "https://localhost:8081/verify";
    internal const string ResetBase = "https://localhost:8081/reset";
    internal const string AcceptedBody = "{\"messageId\":\"<202610041200.1@brevo>\"}";

    // The provider's documented endpoint, written out rather than read from the sender's own constant: a test
    // that asserts against the constant it is testing cannot notice the constant changing.
    internal const string SendUrl = "https://api.brevo.com/v3/smtp/email";

    // The name `AddHttpClient<IMailService, BrevoMailService>` gives its client: the service type's name.
    internal const string TypedClientName = "IMailService";

    // The sender's logging category, which is the only one this suite captures. Without the filter the
    // HttpClient factory's own events arrive too, and "the sender logged nothing" stops being assertable.
    internal const string SenderCategory = "MusicRoom.Infrastructure.Messaging.BrevoMailService";

    /// <summary>The request goes where the provider documents, authenticated by its own header.</summary>
    [Fact]
    public async Task SendEmailVerificationAsync_PostsToTheProviderAsync()
    {
        // Arrange
        (IMailService mail, StubHttpMessageHandler transport, _) = Resolve();

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        CapturedRequest request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Uri?.ToString());
        Assert.Equal(ApiKey, request.ApiKey);
        Assert.Contains(FromAddress, request.Body, StringComparison.Ordinal);
        Assert.Contains(FromName, request.Body, StringComparison.Ordinal);
        Assert.Contains(EmailAddress, request.Body, StringComparison.Ordinal);
        Assert.Contains("Confirm your Music Room address", request.Body, StringComparison.Ordinal);
    }

    /// <summary>The body carries the link, and the link carries the token — SPEC-024 decision 5.</summary>
    [Fact]
    public async Task SendEmailVerificationAsync_CarriesTheConfiguredLinkAsync()
    {
        // Arrange
        (IMailService mail, StubHttpMessageHandler transport, _) = Resolve();

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert — the verification base, not the reset one, and the token inside it.
        string body = Assert.Single(transport.Requests).Body;
        Assert.Contains(VerifyBase, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ResetBase, body, StringComparison.Ordinal);
        Assert.Contains(Token, body, StringComparison.Ordinal);
    }

    /// <summary>The reset message uses its own base URL, which is why the spec added one.</summary>
    [Fact]
    public async Task SendPasswordResetAsync_UsesTheResetBaseUrlAsync()
    {
        // Arrange
        (IMailService mail, StubHttpMessageHandler transport, _) = Resolve();

        // Act
        await mail.SendPasswordResetAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        string body = Assert.Single(transport.Requests).Body;
        Assert.Contains(ResetBase, body, StringComparison.Ordinal);
        Assert.DoesNotContain(VerifyBase, body, StringComparison.Ordinal);
        Assert.Contains("Reset your Music Room password", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no base URL configured the message carries the bare token and composes no link.
    /// </summary>
    /// <remarks>
    /// The same rule <see cref="LoggingMailService"/> follows: a link to a machine nobody intended is worse
    /// than no link, and the flow still completes through the API with the code.
    /// </remarks>
    [Fact]
    public async Task SendEmailVerificationAsync_WithNoBaseUrl_SendsTheCodeAsync()
    {
        // Arrange
        (IMailService mail, StubHttpMessageHandler transport, _) =
            Resolve(verificationLinkBaseUrl: null);

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        string body = Assert.Single(transport.Requests).Body;
        Assert.Contains(Token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("href", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Neither the token nor the key reaches a log event — `secrets-management`, SPEC-024 decision 6.
    /// </summary>
    [Fact]
    public async Task SendEmailVerificationAsync_LogsNeitherTheTokenNorTheKeyAsync()
    {
        // Arrange
        (IMailService mail, _, LoggingMailServiceTests.MessageLog log) = Resolve();

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert — the recipient and the provider's id are logged; the credential is not.
        string message = Assert.Single(log.Messages);
        Assert.Contains(EmailAddress, message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal throws, so `CredentialMailDispatcher` can swallow it and registration still answers `202`.
    /// </summary>
    /// <remarks>
    /// And the provider's body stays out of the exception: an error document may contain anything, including
    /// our own request echoed back — API10.
    /// </remarks>
    [Fact]
    public async Task SendEmailVerificationAsync_WhenTheProviderRefuses_ThrowsWithoutItsBodyAsync()
    {
        // Arrange — a provider error document that names the key, which is exactly what must not propagate.
        const string errorBody = "{\"code\":\"unauthorized\",\"message\":\"key "
            + ApiKey + " is not permitted\"}";
        (IMailService mail, _, LoggingMailServiceTests.MessageLog log) = Resolve(
            status: HttpStatusCode.Unauthorized,
            responseBody: errorBody);

        // Act
        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains(
            ((int)HttpStatusCode.Unauthorized).ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("unauthorized", exception.Message, StringComparison.Ordinal);
        Assert.Empty(log.Messages);
    }

    /// <summary>The send is bounded by default, not left to the provider — SPEC-024 decision 4.</summary>
    [Fact]
    public void MailOptions_BoundTheSendByDefault() =>
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            new MailOptions(MailOptions.BrevoProvider, ApiKey, FromAddress, null, null, null).SendTimeout);

    private static (IMailService Mail, StubHttpMessageHandler Transport, LoggingMailServiceTests.MessageLog Log)
        Resolve(
            string? verificationLinkBaseUrl = VerifyBase,
            HttpStatusCode status = HttpStatusCode.Created,
            string responseBody = AcceptedBody)
    {
        StubHttpMessageHandler transport = new(status, responseBody);
        LoggingMailServiceTests.MessageLog log = new();

        MailOptions options = new(
            MailOptions.BrevoProvider,
            ApiKey,
            FromAddress,
            FromName,
            verificationLinkBaseUrl,
            ResetBase);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(log));

        // Registered through the public composition-root entry point, which is the registration the
        // application itself runs. `isDevelopment: false` is the point: this sender is the one that may.
        services.AddInfrastructureServices(new InfrastructureOptions(
            LoggingMailServiceTests.WorkFactor,
            IsDevelopment: false,
            options,
            new CatalogOptions(
                null,
                TrackRef.DefaultLifetime,
                TrackSearchCache.DefaultLifetime,
                CatalogOptions.DefaultMemoryLifetime,
                CatalogOptions.DefaultRequestTimeout),
            new JwtOptions(
                new string('k', JwtOptions.MinimumSigningKeyBytes),
                "musicroom-tests",
                "musicroom-tests",
                15),
            new CredentialAttemptLimits(
                CredentialAttemptLimits.DefaultPermitsPerWindow,
                TimeSpan.FromMinutes(CredentialAttemptLimits.DefaultWindowMinutes),
                CredentialAttemptLimits.DefaultMaximumPenaltyWindows),
            []));

        // The transport is replaced by the stub, which is the one thing a test may not take from
        // configuration: everything else here is the production registration. Named rather than defaulted,
        // because a default applies before a named client's own configuration and would lose to it — the
        // version of this that used `ConfigureHttpClientDefaults` reached api.brevo.com and was answered
        // `401`, which is a test making a real call and must never pass quietly.
        services.AddHttpClient(TypedClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);

        IMailService mail = services.BuildServiceProvider().GetRequiredService<IMailService>();

        return (mail, transport, log);
    }

    /// <summary>
    /// Feeds every category's events into one <see cref="LoggingMailServiceTests.MessageLog"/>.
    /// </summary>
    /// <remarks>
    /// The sibling suite resolves its sender from a registered factory and hands it a logger directly. This
    /// one resolves a typed client out of a real container, so the capture has to arrive the way the logging
    /// stack delivers it — as a provider.
    /// </remarks>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly LoggingMailServiceTests.MessageLog _log;

        internal CapturingLoggerProvider(LoggingMailServiceTests.MessageLog log) => _log = log;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == SenderCategory
                ? new LoggingMailServiceTests.RecordingLogger<BrevoMailServiceTests>(_log)
                : NullLogger.Instance;

        public void Dispose()
        {
            // Nothing to release: the log outlives this provider on purpose, because the test reads it after
            // the container is gone.
        }
    }
}
