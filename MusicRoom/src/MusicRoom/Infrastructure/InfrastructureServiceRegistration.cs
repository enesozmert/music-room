using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Messaging;
using MusicRoom.Infrastructure.Security;

namespace MusicRoom.Infrastructure;

/// <summary>
/// Registers the external adapters this layer owns.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Registers the password hasher, the access-token issuer and the mail sender.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="bcryptWorkFactor">The cost from configuration. The hasher clamps it up to the
    /// ADR-0007 floor, so a configuration mistake cannot weaken storage.</param>
    /// <param name="isDevelopment">True when the host environment is Development. The only sender
    /// that exists writes the verification link to the log, so this flag decides whether the
    /// application may start at all.</param>
    /// <param name="verificationLinkBaseUrl">Where the client serves the verification screen, from
    /// configuration. Nothing here substitutes a default for it.</param>
    /// <param name="jwtOptions">The signing key, issuer, audience and access-token lifetime, already
    /// validated by <see cref="JwtOptions"/>. They arrive as one record so this method stays inside
    /// the five-parameter limit the .editorconfig enforces.</param>
    /// <returns>The same collection, so calls chain.</returns>
    /// <exception cref="InvalidOperationException">The host is not Development. There is no sender
    /// that may run outside Development yet.</exception>
    /// <remarks>
    /// The cost and the URL arrive as values rather than as <c>IConfiguration</c>: reading them here
    /// would make this project depend on the configuration stack to learn two settings, and the
    /// composition root already holds both.
    /// </remarks>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        int bcryptWorkFactor,
        bool isDevelopment,
        string? verificationLinkBaseUrl,
        JwtOptions jwtOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwtOptions);

        services.AddSingleton<IPasswordHasher>(_ => new BCryptPasswordHasher(bcryptWorkFactor));

        // Singleton: the signing credentials are built once and the issuer holds no request state.
        services.AddSingleton<IAccessTokenIssuer>(_ => new JwtAccessTokenIssuer(jwtOptions));

        // No silent fallback. Outside Development the only available sender would write a live
        // credential into a production log, so the host fails loudly instead — the guard the
        // repository owner accepted for T-26 on 2026-09-26.
        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                "No IMailService may be registered outside Development: the only implementation "
                + "writes the email verification link to the log. The hosted provider is O17, "
                + "slice S1a.");
        }

        services.AddSingleton<IMailService>(provider => new LoggingMailService(
            provider.GetRequiredService<ILogger<LoggingMailService>>(),
            verificationLinkBaseUrl));

        return services;
    }
}
