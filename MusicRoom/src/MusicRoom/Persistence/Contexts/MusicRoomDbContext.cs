using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.Contexts;

/// <summary>
/// The mapped model. Schema ownership belongs to EF Core migrations and to nothing else — SDD §8.5.
/// </summary>
public sealed class MusicRoomDbContext : DbContext
{
    /// <summary>Takes the options the composition root configured.</summary>
    /// <param name="options">Provider and connection options.</param>
    public MusicRoomDbContext(DbContextOptions<MusicRoomDbContext> options)
        : base(options)
    {
    }

    /// <summary>The accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>The outstanding and spent email verification tokens.</summary>
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    /// <summary>Stamps the common columns, then saves — SDD §8.3.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The number of rows written.</returns>
    /// <remarks>
    /// Stamping here rather than in each handler means no aggregate can forget it, and
    /// <c>updated_at_utc</c> stays honest even for a write path added later.
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = DateTime.UtcNow;

        foreach (EntityEntry<Entity> entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = nowUtc;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Applies every <see cref="IEntityTypeConfiguration{TEntity}"/> in this assembly.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MusicRoomDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
