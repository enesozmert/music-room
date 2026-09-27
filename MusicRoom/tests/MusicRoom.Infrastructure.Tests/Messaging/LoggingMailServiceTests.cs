using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Messaging;

/// <summary>
/// The Development mail sender, exercised through the registration that is the only way it is ever
/// created — so this also proves the environment guard the owner accepted for T-26.
/// </summary>
/// <remarks>
/// The sender is resolved from its registered factory rather than constructed directly: the type is
/// internal to <c>MusicRoom.Infrastructure</c>, and the registration is the contract the composition
/// root actually uses.
/// </remarks>
public sealed class LoggingMailServiceTests
{
    internal const string BaseUrl = "https://localhost:8081/verify";
    internal const string EmailAddress = "player@example.com";
    internal const string Token = "a-token-that-stands-in-for-43-base64url-chars";
    internal const int WorkFactor = 12;

    /// <summary>One event, carrying the link a developer is meant to click — O17.</summary>
    [Fact]
    public async Task SendEmailVerificationAsync_WritesTheLinkOnceAsync()
    {
        // Arrange
        MessageLog log = new();
        IMailService mail = ResolveMailService(log, BaseUrl);

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        string message = Assert.Single(log.Messages);
        Assert.Contains(BaseUrl, message, StringComparison.Ordinal);
        Assert.Contains(EmailAddress, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no base URL configured the token is still logged, and the event says why no link was
    /// composed. A hard-coded fallback would print a link to a machine nobody intended.
    /// </summary>
    [Fact]
    public async Task SendEmailVerificationAsync_WithNoBaseUrlConfigured_LogsTheTokenAndSaysSoAsync()
    {
        // Arrange
        MessageLog log = new();
        IMailService mail = ResolveMailService(log, verificationLinkBaseUrl: null);

        // Act
        await mail.SendEmailVerificationAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        string message = Assert.Single(log.Messages);
        Assert.Contains(Token, message, StringComparison.Ordinal);
        Assert.Contains("VERIFICATION_LINK_BASE_URL", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reset token is logged, and the event says why — the widening of T-26 the owner accepted on
    /// 2026-09-27 (SPEC-007 escalation 3). This token replaces a password, so what the sender writes is
    /// asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task SendPasswordResetAsync_WritesTheTokenAndNamesTheRiskAsync()
    {
        // Arrange
        MessageLog log = new();
        IMailService mail = ResolveMailService(log, BaseUrl);

        // Act
        await mail.SendPasswordResetAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        string message = Assert.Single(log.Messages);
        Assert.Contains(Token, message, StringComparison.Ordinal);
        Assert.Contains(EmailAddress, message, StringComparison.Ordinal);
        Assert.Contains("T-26", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reset event is a warning, not information: a credential in a log is worth more attention than
    /// a verification link.
    /// </summary>
    [Fact]
    public async Task SendPasswordResetAsync_LogsAtWarningAsync()
    {
        // Arrange
        MessageLog log = new();
        IMailService mail = ResolveMailService(log, BaseUrl);

        // Act
        await mail.SendPasswordResetAsync(EmailAddress, Token, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Levels));
    }

    /// <summary>
    /// Outside Development the registration refuses to run. The guard is mechanical, not a promise:
    /// no code path writes a verification token into a non-Development log.
    /// </summary>
    [Fact]
    public void AddInfrastructureServices_OutsideDevelopment_Throws()
    {
        // Arrange
        ServiceCollection services = new();

        // Act
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructureServices(Options(isDevelopment: false)));

        // Assert — the message names the decision, not a way to make start-up succeed.
        Assert.Contains("S1a", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Valid options, so the registration under test fails for the reason each test is about and not
    /// because <see cref="JwtOptions"/> refused a short key. SPEC-005 added the JWT settings and
    /// SPEC-006 collapsed the whole parameter list into this record.
    /// </summary>
    private static InfrastructureOptions Options(bool isDevelopment) =>
        new(
            WorkFactor,
            isDevelopment,
            BaseUrl,
            new JwtOptions(
                new string('k', JwtOptions.MinimumSigningKeyBytes),
                "musicroom-tests",
                "musicroom-tests",
                15),
            new CredentialAttemptLimits(
                CredentialAttemptLimits.DefaultPermitsPerWindow,
                TimeSpan.FromMinutes(CredentialAttemptLimits.DefaultWindowMinutes),
                CredentialAttemptLimits.DefaultMaximumPenaltyWindows));

    private static IMailService ResolveMailService(MessageLog log, string? verificationLinkBaseUrl)
    {
        ServiceCollection services = new();
        services.AddInfrastructureServices(Options(isDevelopment: true) with
        {
            VerificationLinkBaseUrl = verificationLinkBaseUrl,
        });

        ServiceDescriptor descriptor = services.Single(
            candidate => candidate.ServiceType == typeof(IMailService));

        Assert.NotNull(descriptor.ImplementationFactory);

        return (IMailService)descriptor.ImplementationFactory(new RecordingLoggerProvider(log));
    }

    /// <summary>What the sender logged, in order.</summary>
    internal sealed class MessageLog
    {
        private readonly List<string> _messages = [];
        private readonly List<LogLevel> _levels = [];

        internal IReadOnlyList<string> Messages => _messages;

        /// <summary>The level of each captured event, in the same order as the messages.</summary>
        internal IReadOnlyList<LogLevel> Levels => _levels;

        internal void Add(string message) => _messages.Add(message);

        /// <summary>Captures one event's message and level together.</summary>
        /// <param name="level">The level it was logged at.</param>
        /// <param name="message">The formatted message.</param>
        internal void Add(LogLevel level, string message)
        {
            _levels.Add(level);
            _messages.Add(message);
        }
    }

    /// <summary>
    /// Answers the one service the registered factory asks for. Hand-written rather than mocked:
    /// this test project pins no mocking package, and one service is all the factory needs.
    /// </summary>
    private sealed class RecordingLoggerProvider : IServiceProvider
    {
        private readonly MessageLog _log;

        internal RecordingLoggerProvider(MessageLog log) => _log = log;

        public object GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            // The factory asks for ILogger<LoggingMailService>, and the category type is internal to
            // MusicRoom.Infrastructure — this project cannot name it. The requested closed generic
            // supplies it instead.
            Type loggerType = typeof(RecordingLogger<>).MakeGenericType(serviceType.GenericTypeArguments);

            return Activator.CreateInstance(loggerType, _log)!;
        }
    }

    /// <summary>Captures formatted log messages for any category.</summary>
    /// <typeparam name="TCategory">The logging category the sender was resolved for.</typeparam>
    /// <remarks>
    /// Internal rather than private: it is constructed reflectively, and the analyzer reads a
    /// private type nothing names as dead code.
    /// </remarks>
    internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly MessageLog _log;

        /// <summary>Public because this type is only ever created reflectively.</summary>
        /// <param name="log">Where formatted messages are collected.</param>
        public RecordingLogger(MessageLog log) => _log = log;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _log.Add(logLevel, formatter(state, exception));
        }
    }
}
