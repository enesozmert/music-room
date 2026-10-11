namespace MusicRoom.Application.Abstractions.Identity;

/// <summary>
/// The clients this provider issues codes to — ADR-0031, SPEC-044 decision 1.
/// </summary>
/// <remarks>
/// **A client is a row of configuration, not a table.** Nobody can self-register with this provider: under
/// the owner's answer A it is a first-party provider, so the registry is a list of the clients this
/// deployment ships, read the way the social provider rows are. A table would be a migration, a seed and an
/// administration surface for a list of one.
/// </remarks>
public interface IOpenIdClientRegistry
{
    /// <summary>Finds a client by the identifier a request spells.</summary>
    /// <param name="clientId">The <c>client_id</c> parameter, as sent.</param>
    /// <returns>The client, or null when this deployment has no such row.</returns>
    OpenIdClient? Find(string clientId);
}

/// <summary>
/// One registered client.
/// </summary>
/// <param name="ClientId">The identifier the client sends as <c>client_id</c>.</param>
/// <param name="RedirectUris">Every URI a code may be delivered to. Exhaustive by design.</param>
/// <remarks>
/// Declared beside the interface because it exists only as its return value — the same rule
/// <c>AuthorizationCodeRedemption</c> follows.
/// </remarks>
public sealed record OpenIdClient(string ClientId, IReadOnlyList<string> RedirectUris)
{
    /// <summary>Whether a code may be delivered to this URI.</summary>
    /// <param name="redirectUri">The <c>redirect_uri</c> parameter, as sent.</param>
    /// <returns>True only for an exact, registered match.</returns>
    /// <remarks>
    /// Exact and case-sensitive, with no prefix rule and no wildcard. A registry that matched prefixes would
    /// make this provider an open redirector for anything under a registered path, which is the vulnerability
    /// the registry exists to prevent — RFC 9700 §2.1.
    /// </remarks>
    public bool Allows(string redirectUri) =>
        RedirectUris.Contains(redirectUri, StringComparer.Ordinal);
}
