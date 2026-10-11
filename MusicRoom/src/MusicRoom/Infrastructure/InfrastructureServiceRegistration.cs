using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Configuration;
using MusicRoom.Infrastructure.Identity;
using MusicRoom.Infrastructure.Messaging;
using MusicRoom.Infrastructure.Security;

namespace MusicRoom.Infrastructure;

/// <summary>
/// Registers the external adapters this layer owns.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Registers the password hasher, the access-token issuer, the credential attempt limiter, the catalog
    /// and the mail sender.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="options">Everything this layer needs from configuration, already validated.</param>
    /// <returns>The same collection, so calls chain.</returns>
    /// <exception cref="InvalidOperationException">No mail sender is configured for this host —
    /// <see cref="MailOptions.Validate"/> states the rule.</exception>
    /// <remarks>
    /// One options record rather than a parameter per setting: SPEC-005 left this method at five
    /// arguments, which is the .editorconfig limit (S107, proven live in S0 at six), and SPEC-006 needs
    /// a sixth. The values still arrive as values rather than as <c>IConfiguration</c>, so this project
    /// does not depend on the configuration stack to learn a number.
    /// </remarks>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Registered as well as constructed at start-up: the composition root needs one before a container
        // exists, and anything resolving the abstraction later must get the same behaviour — SPEC-030.
        // Through the factory since SPEC-047, which memoises per configuration, so what the container hands
        // out is the **same instance** the root read `DATABASE_URL` through — one vault login, one view of
        // the secrets, and no chance of two components disagreeing about what is configured.
        services.AddSingleton<ISecretProvider>(provider =>
            SecretProviderFactory.For(provider.GetRequiredService<IConfiguration>()));

        services.AddSingleton<IPasswordHasher>(_ => new BCryptPasswordHasher(options.BCryptWorkFactor));

        // Singleton because the set is read from an embedded resource once: 46,146 entries into a
        // case-insensitive hash set at start-up, one lookup per password choice, nothing per request —
        // SPEC-035, ADR-0042. A build without the resource throws here rather than screening nothing.
        services.AddSingleton<IBreachedPasswordSet>(_ => new BundledBreachedPasswordSet());

        // Singleton: the signing credentials are built once and the issuer holds no request state.
        services.AddSingleton<IAccessTokenIssuer>(_ => new JwtAccessTokenIssuer(options.Jwt));

        // The RS256 pair this API signs identity assertions with — ADR-0031, O19. Singleton because the key
        // is read once and a key that changed between requests would invalidate what the last one signed.
        services.AddSingleton<ISigningKeyProvider>(provider =>
            new RsaSigningKeyProvider(provider.GetRequiredService<ISecretProvider>()));

        // The provider's own two pieces — SPEC-044. The clients are configuration rather than a table, so
        // the registry reads them once; the ID token issuer holds the signing credentials and no request
        // state. Both read through the abstractions above rather than growing `InfrastructureOptions`, which
        // is already past the argument limit this method answers with one record.
        services.AddSingleton<IOpenIdClientRegistry>(provider =>
            new ConfiguredOpenIdClientRegistry(provider.GetRequiredService<ISecretProvider>()));

        services.AddSingleton<IIdTokenIssuer>(provider =>
            new IdTokenIssuer(provider.GetRequiredService<ISigningKeyProvider>()));

        // Singleton on purpose: the attempt history is the whole point, and a scoped limiter would
        // forget every failure at the end of the request that recorded it.
        services.AddSingleton<ICredentialAttemptLimiter>(_ =>
            new InMemoryCredentialAttemptLimiter(options.CredentialAttempts, TimeProvider.System));

        // No silent fallback, and no host without a sender. SPEC-024 replaced the Development-only guard
        // with this one: the configuration names a provider, and start-up fails when it names nothing
        // valid for this environment — T-26, ADR-0013.
        MailServiceRegistration.Add(services, options.Mail, options.IsDevelopment);

        // The catalog: the provider client with its resilience pipeline, and ADR-0023's two cache levels in
        // front of it. Delegated for the same reason the mail sender is — this method is at the argument
        // limit, and a provider concern belongs with its own wiring.
        CatalogRegistration.Add(services, options.Catalog);

        // The relying-party core and its provider rows — ADR-0008, SPEC-041. An empty list is allowed: a host
        // with no provider credentials still boots and still signs people in with a password.
        SocialIdentityRegistration.Add(services, options.SocialProviders);

        return services;
    }
}

/// <summary>
/// Everything <see cref="InfrastructureServiceRegistration.AddInfrastructureServices"/> needs from the
/// composition root.
/// </summary>
/// <param name="BCryptWorkFactor">The cost from configuration. The hasher clamps it up to the ADR-0007
/// floor, so a configuration mistake cannot weaken storage.</param>
/// <param name="IsDevelopment">True when the host environment is Development. One sender — the one that
/// writes the verification link and the reset token to the log — may run there and nowhere else.</param>
/// <param name="Mail">Which sender to register and what it needs, including the two link base URLs.
/// Nothing here substitutes a default for either of them.</param>
/// <param name="Catalog">The YouTube key and the two cache lifetimes — ADR-0003, ADR-0023.</param>
/// <param name="Jwt">The signing key, issuer, audience and access-token lifetime.</param>
/// <param name="CredentialAttempts">The per-account credential limits — SPEC-006 escalation 1.</param>
/// <param name="SocialProviders">The configured provider rows — ADR-0008's "a provider is a row", SPEC-041.
/// Empty when no provider credentials are set, which is a host that signs people in with a password.</param>
/// <remarks>
/// Declared beside the registration rather than in its own file: it exists only as that method's
/// parameter list, and SPEC-006's Files table is the contract for what this change may create.
/// </remarks>
public sealed record InfrastructureOptions(
    int BCryptWorkFactor,
    bool IsDevelopment,
    MailOptions Mail,
    CatalogOptions Catalog,
    JwtOptions Jwt,
    CredentialAttemptLimits CredentialAttempts,
    IReadOnlyList<SocialProviderOptions> SocialProviders);
