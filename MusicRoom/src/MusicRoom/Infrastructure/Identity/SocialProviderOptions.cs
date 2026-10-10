namespace MusicRoom.Infrastructure.Identity;

/// <summary>
/// One provider, as a row of configuration — ADR-0008's amendment, SPEC-041.
/// </summary>
/// <remarks>
/// The owner's requirement of 2026-09-27 was *"one relying-party implementation, driven by configuration, not
/// one integration per provider"*. This record is what "a row" means: everything that differs between Google
/// and Facebook, and nothing that does not.
/// <para>
/// `ClientSecret` is nullable because it is not universal. Google's authorization code flow needs it to
/// exchange a code; Facebook's Limited Login issues an ID token with no exchange at all, so a row for it
/// carries none. A provider whose row has no secret cannot be exchanged with, and the core says so rather than
/// sending an empty string to a token endpoint.
/// </para>
/// </remarks>
/// <param name="Name">The row's name, lower case — what the API's own request body names.</param>
/// <param name="DiscoveryUrl">Where the OpenID Connect discovery document lives.</param>
/// <param name="Issuer">The `iss` every token from this provider must carry, compared exactly.</param>
/// <param name="ClientId">The `aud` every token must carry, compared exactly. An ID token minted for another
/// application is rejected here — ADR-0008's token-substitution check.</param>
/// <param name="ClientSecret">The secret for the code exchange, or null for a provider that issues an ID
/// token without one.</param>
public sealed record SocialProviderOptions(
    string Name,
    Uri DiscoveryUrl,
    string Issuer,
    string ClientId,
    string? ClientSecret);
