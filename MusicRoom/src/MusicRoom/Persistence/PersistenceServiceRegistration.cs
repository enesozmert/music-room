using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
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

        IdentityRepositories.Add(services);
        CollaborationRepositories.Add(services);
        PlaybackRepositories.Add(services);
        CatalogRepositories.Add(services);

        services.AddScoped<IAuditSink, AuditSink>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}

/// <summary>The identity and profile read and write paths.</summary>
/// <remarks>
/// Grouped by context rather than listed in one method, which the coupling gate caps at 30 types — the same
/// reason <c>CallerServices</c> and <c>PlaylistChildRoutes</c> exist. The grouping follows SDD §5.2's bounded
/// contexts, so a new repository has one obvious home.
/// </remarks>
internal static class IdentityRepositories
{
    /// <summary>Registers them.</summary>
    /// <param name="services">The container being built.</param>
    internal static void Add(IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<ISocialLinkRepository, SocialLinkRepository>();

        // Its own registration because it is its own file: UserRepository is at 293 code lines of the 300 S104
        // enforces (issue O27), and the reset path's context read goes beside it rather than into it — SPEC-035.
        services.AddScoped<IPasswordContextReader, PasswordContextRepository>();
    }
}

/// <summary>The playlist and event read and write paths — the two collaboration contexts.</summary>
internal static class CollaborationRepositories
{
    /// <summary>Registers them.</summary>
    /// <param name="services">The container being built.</param>
    internal static void Add(IServiceCollection services)
    {
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IPlaylistTrackRepository, PlaylistTrackRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IEventQueueRepository, EventQueueRepository>();
        services.AddScoped<IEventCodeRepository, EventCodeRepository>();
    }
}

/// <summary>The device and playback read and write paths.</summary>
internal static class PlaybackRepositories
{
    /// <summary>Registers them.</summary>
    /// <param name="services">The container being built.</param>
    internal static void Add(IServiceCollection services)
    {
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IPlaybackSessionRepository, PlaybackSessionRepository>();
        services.AddScoped<IPlaybackQueueReader, PlaybackQueueReader>();
    }
}

/// <summary>The catalog's persistent cache — the fourth context, ADR-0023's L2.</summary>
/// <remarks>
/// Its own group for the same reason the other three have one: the coupling gate caps a single registration
/// method at 30 types, and a new repository should have one obvious home.
/// </remarks>
internal static class CatalogRepositories
{
    /// <summary>Registers them.</summary>
    /// <param name="services">The container being built.</param>
    internal static void Add(IServiceCollection services) =>
        services.AddScoped<ITrackRefRepository, TrackRefRepository>();
}
