using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="TrackRef"/> to <c>track_refs</c> — the persistent half of ADR-0023's cache.
/// </summary>
internal sealed class TrackRefConfiguration : IEntityTypeConfiguration<TrackRef>
{
    /// <summary>The width of a YouTube video id column, as the suggestion and track tables already use.</summary>
    internal const int VideoIdLength = 32;

    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<TrackRef> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("track_refs");
        builder.HasKey(track => track.Id);

        builder.Property(track => track.Id).HasColumnName("id");

        builder.Property(track => track.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(VideoIdLength)
            .IsRequired();

        // Generous widths: these are provider-supplied strings, and a title truncated mid-word is a defect a
        // user sees. The provider's own limits are well inside these.
        builder.Property(track => track.Title)
            .HasColumnName("title")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(track => track.Channel)
            .HasColumnName("channel")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(track => track.DurationSeconds).HasColumnName("duration_seconds");

        builder.Property(track => track.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(track => track.FetchedAtUtc)
            .HasColumnName("fetched_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(track => track.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(track => track.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // One row per video, enforced by the database. Two requests resolving the same id at once is the
        // ordinary case here, so the index is what arbitrates rather than a read-then-write in the repository.
        builder.HasIndex(track => track.YoutubeVideoId)
            .IsUnique()
            .HasDatabaseName("ux_track_refs_youtube_video_id");
    }
}
