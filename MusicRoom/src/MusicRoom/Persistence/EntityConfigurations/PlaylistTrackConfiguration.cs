using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="PlaylistTrack"/> to <c>playlist_tracks</c>.
/// </summary>
/// <remarks>
/// The index on <c>(playlist_id, position)</c> is deliberately **not** unique. A renumber rewrites every row
/// of one playlist in one batch, and a unique constraint would be checked statement by statement inside that
/// batch, so an ascending rewrite would collide with positions it has not reached yet. Nothing needs the
/// uniqueness: every track write first updates the playlist row, so two writers never assign a position at
/// the same time — the loser is already a `412`.
/// </remarks>
internal sealed class PlaylistTrackConfiguration : IEntityTypeConfiguration<PlaylistTrack>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<PlaylistTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_tracks");
        builder.HasKey(track => track.Id);

        builder.Property(track => track.Id).HasColumnName("id");
        builder.Property(track => track.PlaylistId).HasColumnName("playlist_id");

        builder.Property(track => track.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(track => track.Position)
            .HasColumnName("position")
            .IsRequired();

        builder.Property(track => track.AddedByUserId).HasColumnName("added_by_user_id");

        builder.Property(track => track.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(track => track.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(track => new { track.PlaylistId, track.Position })
            .HasDatabaseName("ix_playlist_tracks_playlist_id_position");

        // Declared rather than left to EF's own foreign-key index, whose PascalCase name the schema
        // convention tests reject.
        builder.HasIndex(track => track.AddedByUserId)
            .HasDatabaseName("ix_playlist_tracks_added_by_user_id");

        builder.HasOne<Playlist>()
            .WithMany()
            .HasForeignKey(track => track.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(track => track.AddedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
