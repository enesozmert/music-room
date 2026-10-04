using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// Chooses the mail sender this host runs — ADR-0013, SPEC-024 decision 2.
/// </summary>
/// <remarks>
/// Its own class rather than more lines in <c>InfrastructureServiceRegistration</c>, which is already at the
/// five-argument limit `SonarLint.xml` enforces.
/// <para>
/// The guard this replaces asked "is this a Development host?" and threw if it was not, because the only sender
/// wrote a live credential to the log (T-26). The guard now asks "is a sender configured?" —
/// <see cref="MailOptions.Validate"/> answers it — so a production host boots when, and only when, it has a
/// real provider. A typo in <c>MAIL_PROVIDER</c> stops start-up instead of silently downgrading the host to the
/// sender that logs tokens.
/// </para>
/// </remarks>
internal static class MailServiceRegistration
{
    /// <summary>Brevo's API host.</summary>
    /// <remarks>
    /// Trailing slash on purpose: <c>HttpClient</c> resolves a relative request URI against the base address
    /// and drops the last segment when the base has no trailing slash.
    /// </remarks>
    internal const string BrevoBaseAddress = "https://api.brevo.com/";

    /// <summary>The header Brevo authenticates with. Not <c>Authorization</c>, by the provider's design.</summary>
    internal const string ApiKeyHeader = "api-key";

    /// <summary>Registers the sender the configuration names.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="options">The mail settings, already read from configuration.</param>
    /// <param name="isDevelopment">True when the host environment is Development.</param>
    /// <exception cref="InvalidOperationException">No valid sender is configured for this host.</exception>
    internal static void Add(IServiceCollection services, MailOptions options, bool isDevelopment)
    {
        // Throws before anything is registered, so a misconfigured host fails at start-up rather than on its
        // first registration — when the account row is already written and the message is already lost.
        options.Validate(isDevelopment);

        if (options.Provider == MailOptions.LoggingProvider)
        {
            services.AddSingleton<IMailService>(provider => new LoggingMailService(
                provider.GetRequiredService<ILogger<LoggingMailService>>(),
                options.VerificationLinkBaseUrl));

            return;
        }

        services
            .AddHttpClient<IMailService, BrevoMailService>(client =>
            {
                client.BaseAddress = new Uri(BrevoBaseAddress);
                client.Timeout = options.SendTimeout;
                client.DefaultRequestHeaders.Add(ApiKeyHeader, options.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // A provider that answers with a redirect is a provider answering something this sender did
                // not ask for, and following it would send the API key to wherever it points — API10.
                AllowAutoRedirect = false,
            });

        services.AddSingleton(options);
    }
}
