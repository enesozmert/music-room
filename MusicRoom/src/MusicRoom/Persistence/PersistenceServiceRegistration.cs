using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Persistence.Auditing;
using MusicRoom.Persistence.Contexts;
using MusicRoom.Persistence.Repositories;

namespace MusicRoom.Persistence;

/// <summary>
/// Registers the context and the repository implementations.
/// </summary>
public static class PersistenceServiceRegistration
{
    /// <summary>
    /// Registers <see cref="MusicRoomDbContext"/> against PostgreSQL and the repositories that use it.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="connectionString">The value of <c>DATABASE_URL</c> — ADR-0026.</param>
    /// <returns>The same collection, so calls chain.</returns>
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<MusicRoomDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IAuditSink, AuditSink>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
