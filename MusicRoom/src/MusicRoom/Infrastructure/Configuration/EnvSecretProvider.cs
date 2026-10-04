using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;

namespace MusicRoom.Infrastructure.Configuration;

/// <summary>
/// The process environment, which <c>docker compose</c> fills from `.env` — ADR-0029's default provider.
/// </summary>
/// <remarks>
/// A thin adapter over <see cref="IConfiguration"/> rather than over
/// <c>Environment.GetEnvironmentVariable</c>, deliberately: the configuration stack already layers environment
/// variables, and it is also what a test host fills through <c>UseSetting</c>. Reading the environment directly
/// would work in production and silently ignore every test's configuration.
/// <para>
/// Public, unlike the other adapters in this project, because the composition root constructs one **before**
/// the container exists: the start-up guards read `DATABASE_URL` and the JWT settings while services are still
/// being registered. It is registered as well, so anything resolving <see cref="ISecretProvider"/> later gets
/// the same behaviour.
/// </para>
/// <para>
/// Nothing here logs. A provider that traced its reads would trace the signing key.
/// </para>
/// </remarks>
public sealed class EnvSecretProvider : ISecretProvider
{
    private readonly IConfiguration _configuration;

    /// <summary>Takes the configuration the host built.</summary>
    /// <param name="configuration">The layered configuration, environment variables included.</param>
    public EnvSecretProvider(IConfiguration configuration) => _configuration = configuration;

    /// <inheritdoc />
    public string Require(string name)
    {
        string? value = Read(name);

        return value ?? throw new InvalidOperationException(
            $"{name} is not set. Copy .env.example to .env and fill it in — see ADR-0026.");
    }

    /// <inheritdoc />
    public string? Read(string name)
    {
        string? value = _configuration[name];

        // Blank is absent: a host that sets `MAIL_PROVIDER=` has configured nothing, and treating the empty
        // string as a value would select a sender named "" instead of refusing to start.
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Blank goes through <see cref="Read"/> first, so it is absent and the fallback applies — the rule this
    /// class exists to decide once. A value that is present and unparseable still throws, because
    /// `RATE_LIMIT_ACCOUNT_PERMITS=abc` is a mistake a host should hear about at boot.
    /// </remarks>
    public int ReadInteger(string name, int fallback) =>
        Read(name) is null ? fallback : _configuration.GetValue(name, fallback);

    /// <inheritdoc />
    /// <remarks>Blank is absent, as for <see cref="ReadInteger"/>.</remarks>
    public bool ReadBoolean(string name, bool fallback) =>
        Read(name) is null ? fallback : _configuration.GetValue(name, fallback);
}
