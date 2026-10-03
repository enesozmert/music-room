using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="TrackSearchCache"/> to <c>track_search_cache</c> — SPEC-025 decision 3.
/// </summary>
/// <remarks>
/// The id list is a PostgreSQL <c>text[]</c> rather than a child table. It is read and written whole, nothing
/// joins to it, and the order is the provider's ranking — a child table would add a row per result and a sort
/// column to reproduce something the array already keeps.
/// </remarks>
internal sealed class TrackSearchCacheConfiguration : IEntityTypeConfiguration<TrackSearchCache>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<TrackSearchCache> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_search_cache");
        builder.HasKey(search => search.Id);

        builder.Property(search => search.Id).HasColumnName("id");

        builder.Property(search => search.Query)
            .HasColumnName("query")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(search => search.VideoIds)
            .HasColumnName("video_ids")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(search => search.FetchedAtUtc)
            .HasColumnName("fetched_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(search => search.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(search => search.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // One row per normalised query, so the same search cannot be stored twice and spend `search.list` twice.
        builder.HasIndex(search => search.Query)
            .IsUnique()
            .HasDatabaseName("ux_track_search_cache_query");
    }
}
