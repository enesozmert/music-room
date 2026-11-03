using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="PlaybackPosition"/> to <c>playback_positions</c> — SPEC-059.
/// </summary>
internal sealed class PlaybackPositionConfiguration : IEntityTypeConfiguration<PlaybackPosition>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<PlaybackPosition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playback_positions");
        builder.HasKey(position => position.Id);

        builder.Property(position => position.Id).HasColumnName("id");
        builder.Property(position => position.SessionId).HasColumnName("session_id");
        builder.Property(position => position.NowPlayingId).HasColumnName("now_playing_id");
        builder.Property(position => position.PositionMs).HasColumnName("position_ms");
        builder.Property(position => position.Paused).HasColumnName("paused");

        builder.Property(position => position.ReportedAtUtc)
            .HasColumnName("reported_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(position => position.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(position => position.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // One row per session, which is what makes a report an update rather than a growing log. The
        // index is the thing that enforces it; without it a dropped connection mid-upsert would leave two.
        builder.HasIndex(position => position.SessionId)
            .IsUnique()
            .HasDatabaseName("ux_playback_positions_session_id");

        // Ending a session takes its position with it. No navigation property — the row is read through
        // the session it names and never traversed from it.
        builder.HasOne<PlaybackSession>()
            .WithMany()
            .HasForeignKey(position => position.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
