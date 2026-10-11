using MusicRoom.Application.Abstractions.Configuration;

namespace MusicRoom.Infrastructure.Configuration;

/// <summary>
/// The vault's answers, with the process environment underneath them — ADR-0029, SPEC-047.
/// </summary>
/// <remarks>
/// **The vault wins; `.env` is the floor.** A name the vault holds comes from the vault. A name it does not
/// hold falls through to the environment, and that is forced rather than hedged: `INFISICAL_CLIENT_ID` and
/// `INFISICAL_CLIENT_SECRET` are the credentials this provider logs in with, so they cannot live in the
/// vault they open. ADR-0029 said it before any of this was built — *"a vault does not remove the local
/// secret file; it shrinks it."*
/// <para>
/// What the layering buys is that **moving a secret into the vault is a vault operation**: no code changes,
/// no registration changes, and nothing above this class can tell where a value came from.
/// </para>
/// <para>
/// **Read once, at start-up.** The dictionary is whatever the vault held when the host booted, so a read
/// costs nothing and a vault outage is a boot failure rather than a request failure. **Rotating a secret
/// therefore needs a restart** — the honest cost, and a cheap one for a container.
/// </para>
/// <para>
/// Nothing here logs. A provider that traced its reads would trace the signing key.
/// </para>
/// </remarks>
public sealed class InfisicalSecretProvider : ISecretProvider
{
    private readonly IReadOnlyDictionary<string, string> _secrets;
    private readonly ISecretProvider _environment;

    /// <summary>Takes what the vault answered and the provider to fall through to.</summary>
    /// <param name="secrets">The vault's secrets, by name, as read at start-up.</param>
    /// <param name="environment">The environment provider — the floor under the vault.</param>
    public InfisicalSecretProvider(
        IReadOnlyDictionary<string, string> secrets,
        ISecretProvider environment)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(environment);

        _secrets = secrets;
        _environment = environment;
    }

    /// <summary>How many names the vault answered with — the one thing about them that may be logged.</summary>
    /// <remarks>
    /// A count, not the names: a list of secret names tells a reader what this deployment holds, and a log
    /// line is the wrong place to publish an inventory.
    /// </remarks>
    public int Count => _secrets.Count;

    /// <inheritdoc />
    public string Require(string name)
    {
        string? value = Read(name);

        return value ?? throw new InvalidOperationException(
            $"{name} is set neither in the vault nor in the environment. Add it to the Infisical project "
            + "INFISICAL_PROJECT_ID names, in environment INFISICAL_ENVIRONMENT, and restart — secrets are "
            + "read once at start-up (SPEC-047).");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Blank reads as absent, which is the rule <see cref="EnvSecretProvider" /> already applies: a secret
    /// stored in the vault with an empty value has configured nothing. It falls through, rather than
    /// shadowing an environment value with emptiness — otherwise adding an empty secret to a vault would
    /// silently unset a working setting.
    /// </remarks>
    public string? Read(string name)
    {
        if (_secrets.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return _environment.Read(name);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parsed here when the vault answered, and delegated when it did not — so a number in the vault and a
    /// number in `.env` behave identically, including the part where an unparseable value stops start-up.
    /// </remarks>
    public int ReadInteger(string name, int fallback)
    {
        string? value = Read(name);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{name} is not a number. A value that is present and unparseable is a mistake a host should "
                + "hear about at boot rather than silently run without — SPEC-030.");
    }

    /// <inheritdoc />
    /// <remarks>Same rule as <see cref="ReadInteger" />, for the same reason.</remarks>
    public bool ReadBoolean(string name, bool fallback)
    {
        string? value = Read(name);

        if (value is null)
        {
            return fallback;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{name} is not a boolean. Use `true` or `false` — SPEC-030.");
    }
}
