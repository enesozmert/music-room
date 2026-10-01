using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="Playlist"/> to <c>playlists</c>.
/// </summary>
internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        const int nameLength = 120;

        builder.ToTable("playlists");
        builder.HasKey(playlist => playlist.Id);

        builder.Property(playlist => playlist.Id).HasColumnName("id");
        builder.Property(playlist => playlist.OwnerId).HasColumnName("owner_id");

        builder.Property(playlist => playlist.Name)
            .HasColumnName("name")
            .HasMaxLength(nameLength)
            .IsRequired();

        builder.Property(playlist => playlist.Visibility)
            .HasColumnName("visibility")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(playlist => playlist.Licence)
            .HasColumnName("licence")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(playlist => playlist.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(playlist => playlist.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(playlist => playlist.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // PostgreSQL's own row version as the concurrency token — SDD §12.2. No column is created and nothing
        // in the application increments it, which is the point: a token the code maintains is a token the code
        // can forget to maintain.
        builder.Property(playlist => playlist.Version)
            .HasColumnName("xmin")
            .IsRowVersion();

        // Soft delete, applied by EF to every query on this type. A forgotten `WHERE` therefore cannot
        // resurrect a deleted playlist — SPEC-014 decision 2.
        builder.HasQueryFilter(playlist => playlist.DeletedAtUtc == null);

        builder.HasIndex(playlist => playlist.OwnerId)
            .HasDatabaseName("ix_playlists_owner_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(playlist => playlist.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
