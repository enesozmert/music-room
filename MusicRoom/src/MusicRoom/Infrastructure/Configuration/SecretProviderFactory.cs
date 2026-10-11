using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;

namespace MusicRoom.Infrastructure.Configuration;

/// <summary>
/// Chooses where configuration comes from — ADR-0029's two providers, SPEC-047.
/// </summary>
/// <remarks>
/// **`SECRET_PROVIDER` selects, and a typo stops the host.** `env` is the default and `infisical` is the
/// vault; anything else is refused by name, because a value nobody recognised must not silently become the
/// default — that would be a vault nobody noticed was not being used. The same shape `MAIL_PROVIDER` has.
/// <para>
/// **One instance per configuration.** The composition root reads several values before a container exists —
/// `DATABASE_URL`, the JWT settings, the mail provider — and each read calls this. Without memoising, a
/// vault-backed host would log in once per read and report a different failure each time. The table is keyed
/// on the configuration object and holds weak references, so a test host's provider dies with it.
/// </para>
/// <para>
/// The vault's fetch is awaited synchronously, which is correct exactly here: this runs during start-up,
/// before the host is listening, and a configuration read is not something the rest of the code can be made
/// asynchronous for.
/// </para>
/// </remarks>
public static class SecretProviderFactory
{
    /// <summary>The variable that selects a provider.</summary>
    public const string SelectorVariable = "SECRET_PROVIDER";

    /// <summary>The default: the process environment, which `docker compose` fills from `.env`.</summary>
    public const string EnvironmentProvider = "env";

    /// <summary>The vault SPEC-042 runs in this stack.</summary>
    public const string VaultProvider = "infisical";

    private static readonly ConditionalWeakTable<IConfiguration, ISecretProvider> _created = new();

    /// <summary>Returns the provider this configuration selects, building it at most once.</summary>
    /// <param name="configuration">The layered configuration the host built.</param>
    /// <returns>The provider every later read goes through.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>SECRET_PROVIDER</c> names something that is not a provider, or it names the vault and the vault
    /// refused, could not be reached, or is missing one of its five bootstrap variables.
    /// </exception>
    public static ISecretProvider For(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        lock (_created)
        {
            if (_created.TryGetValue(configuration, out ISecretProvider? existing))
            {
                return existing;
            }

            ISecretProvider created = Build(configuration);
            _created.Add(configuration, created);

            return created;
        }
    }

    private static ISecretProvider Build(IConfiguration configuration)
    {
        EnvSecretProvider environment = new(configuration);
        string selected = environment.Read(SelectorVariable) ?? EnvironmentProvider;

        if (string.Equals(selected, EnvironmentProvider, StringComparison.OrdinalIgnoreCase))
        {
            return environment;
        }

        if (!string.Equals(selected, VaultProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Named rather than defaulted: a host that meant to read from the vault and misspelled the
            // selector would otherwise boot happily on the file the vault exists to replace.
            throw new InvalidOperationException(
                $"{SelectorVariable} is not a provider this application has. Use `{EnvironmentProvider}` "
                + $"or `{VaultProvider}` — ADR-0029, SPEC-047.");
        }

        return FromVault(environment);
    }

    /// <summary>Logs in, fetches once, and puts the environment underneath what came back.</summary>
    private static ISecretProvider FromVault(EnvSecretProvider environment)
    {
        InfisicalOptions options = new(
            new Uri(environment.Require("INFISICAL_SITE_URL")),
            environment.Require("INFISICAL_CLIENT_ID"),
            environment.Require("INFISICAL_CLIENT_SECRET"),
            environment.Require("INFISICAL_PROJECT_ID"),
            environment.Require("INFISICAL_ENVIRONMENT"));

        using HttpClient client = new() { BaseAddress = options.SiteUrl, Timeout = VaultTimeout };

        IReadOnlyDictionary<string, string> secrets = new InfisicalVaultClient(client, options)
            .FetchAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return new InfisicalSecretProvider(secrets, environment);
    }

    /// <summary>How long start-up waits for the vault.</summary>
    /// <remarks>
    /// Ten seconds: long enough for a container that has just come up alongside this one, short enough that
    /// a vault which is not there fails the boot while somebody is still watching it.
    /// </remarks>
    private static TimeSpan VaultTimeout => TimeSpan.FromSeconds(10);
}
