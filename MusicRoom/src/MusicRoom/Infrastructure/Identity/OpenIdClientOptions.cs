using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Application.Abstractions.Identity;

namespace MusicRoom.Infrastructure.Identity;

/// <summary>
/// The registered clients, read from configuration — SPEC-044 decision 1.
/// </summary>
/// <remarks>
/// **Two variables, one client.** `OIDC_CLIENT_ID` names it and `OIDC_CLIENT_REDIRECT_URIS` lists the URIs a
/// code may be delivered to, separated by commas or whitespace. A second client is two more variables and no
/// edit to this file, which is the shape ADR-0008's amendment settled for the social provider rows.
/// <para>
/// **A deployment with no client row boots.** Every authorization request then answers `400 CLIENT.UNKNOWN`,
/// which is the honest answer: this host has no client. Refusing to start would make the mobile client's
/// identifier a requirement for running the API at all, and the password endpoints do not need it. The same
/// reasoning <c>SocialProviderConfiguration</c> records.
/// </para>
/// </remarks>
public sealed class ConfiguredOpenIdClientRegistry : IOpenIdClientRegistry
{
    /// <summary>The variable naming the client this deployment ships.</summary>
    public const string ClientIdVariable = "OIDC_CLIENT_ID";

    /// <summary>The variable listing that client's redirect URIs.</summary>
    public const string RedirectUrisVariable = "OIDC_CLIENT_REDIRECT_URIS";

    private static readonly char[] _separators = [',', ' ', '\t', '\n', '\r'];

    private readonly IReadOnlyDictionary<string, OpenIdClient> _rows;

    /// <summary>Reads the rows once.</summary>
    /// <param name="secrets">Where configuration comes from — SPEC-030's one abstraction.</param>
    public ConfiguredOpenIdClientRegistry(ISecretProvider secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        _rows = Read(secrets);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordinal and case-sensitive: a client identifier is an opaque string, and two identifiers that differ
    /// only in case are two identifiers.
    /// </remarks>
    public OpenIdClient? Find(string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);

        return _rows.TryGetValue(clientId, out OpenIdClient? client) ? client : null;
    }

    private static IReadOnlyDictionary<string, OpenIdClient> Read(ISecretProvider secrets)
    {
        string? clientId = secrets.Read(ClientIdVariable);
        string? redirectUris = secrets.Read(RedirectUrisVariable);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUris))
        {
            return new Dictionary<string, OpenIdClient>(StringComparer.Ordinal);
        }

        string[] uris = redirectUris.Split(_separators, StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);

        // A client with an identifier and no usable URI is not a client: a code it could not be sent to is
        // a flow nobody can finish, and an empty list would make every redirect unregistered anyway.
        return uris.Length == 0
            ? new Dictionary<string, OpenIdClient>(StringComparer.Ordinal)
            : new Dictionary<string, OpenIdClient>(StringComparer.Ordinal)
            {
                [clientId] = new OpenIdClient(clientId, uris),
            };
    }
}
