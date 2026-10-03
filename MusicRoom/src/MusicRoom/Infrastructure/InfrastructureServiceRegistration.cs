using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Catalog;
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

        services.AddSingleton<IPasswordHasher>(_ => new BCryptPasswordHasher(options.BCryptWorkFactor));

        // Singleton: the signing credentials are built once and the issuer holds no request state.
        services.AddSingleton<IAccessTokenIssuer>(_ => new JwtAccessTokenIssuer(options.Jwt));

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
    CredentialAttemptLimits CredentialAttempts);
