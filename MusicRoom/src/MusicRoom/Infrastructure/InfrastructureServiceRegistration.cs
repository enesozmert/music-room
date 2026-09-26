using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;

namespace MusicRoom.Infrastructure;

/// <summary>
/// Registers the external adapters this layer owns.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Registers the password hasher.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="bcryptWorkFactor">The cost from configuration. The hasher clamps it up to the
    /// ADR-0007 floor, so a configuration mistake cannot weaken storage.</param>
    /// <returns>The same collection, so calls chain.</returns>
    /// <remarks>
    /// The cost arrives as a number rather than as <c>IConfiguration</c>: reading it here would make
    /// this project depend on the configuration stack to learn one integer, and the composition root
    /// already holds it.
    /// </remarks>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        int bcryptWorkFactor)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPasswordHasher>(_ => new BCryptPasswordHasher(bcryptWorkFactor));

        return services;
    }
}
