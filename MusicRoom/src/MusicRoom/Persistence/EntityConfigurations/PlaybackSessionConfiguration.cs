using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="PlaybackSession"/> to <c>playback_sessions</c>.
/// </summary>
/// <remarks>
/// The unique index on <c>device_id</c> is **filtered** on <c>ended_at_utc IS NULL</c>, which is how ADR-0017's
/// "at most one active session per device" becomes a database constraint rather than a check somebody can forget.
/// A stopped session keeps its row.
/// <para>
/// There is no foreign key on <c>source_id</c>: one column pair for two possible parents is the honest shape, and
/// two nullable keys would allow a row naming both or neither — SPEC-021 decision 3.
/// </para>
/// </remarks>
internal sealed class PlaybackSessionConfiguration : IEntityTypeConfiguration<PlaybackSession>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<PlaybackSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playback_sessions");
        builder.HasKey(session => session.Id);

        MapColumns(builder);
        MapIndexesAndKeys(builder);
    }

    private static void MapColumns(EntityTypeBuilder<PlaybackSession> builder)
    {
        builder.Property(session => session.Id).HasColumnName("id");
        builder.Property(session => session.DeviceId).HasColumnName("device_id");

        builder.Property(session => session.SourceType)
            .HasColumnName("source_type")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(session => session.SourceId).HasColumnName("source_id");
        builder.Property(session => session.NowPlayingId).HasColumnName("now_playing_id");

        builder.Property(session => session.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(session => session.EndedAtUtc)
            .HasColumnName("ended_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(session => session.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // PostgreSQL's own row version, as every other guarded aggregate here uses it. This is the token that
        // resolves ADR-0017's fourth hotspot: two clients advancing at once give one move and one `412`.
        builder.Property(session => session.Version)
            .HasColumnName("xmin")
            .IsRowVersion();
    }

    private static void MapIndexesAndKeys(EntityTypeBuilder<PlaybackSession> builder)
    {
        // One **live** session per device. The filter is what lets history accumulate behind it.
        builder.HasIndex(session => session.DeviceId)
            .IsUnique()
            .HasFilter("ended_at_utc IS NULL")
            .HasDatabaseName("ux_playback_sessions_device_id");

        // The queue read and the hub both ask "what is playing for this source".
        builder.HasIndex(session => session.SourceId)
            .HasDatabaseName("ix_playback_sessions_source_id");

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(session => session.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
