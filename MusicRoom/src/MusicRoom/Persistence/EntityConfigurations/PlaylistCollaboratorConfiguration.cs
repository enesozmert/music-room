using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="PlaylistCollaborator"/> to <c>playlist_collaborators</c>.
/// </summary>
internal sealed class PlaylistCollaboratorConfiguration : IEntityTypeConfiguration<PlaylistCollaborator>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<PlaylistCollaborator> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("playlist_collaborators");
        builder.HasKey(collaborator => collaborator.Id);

        builder.Property(collaborator => collaborator.Id).HasColumnName("id");
        builder.Property(collaborator => collaborator.PlaylistId).HasColumnName("playlist_id");
        builder.Property(collaborator => collaborator.UserId).HasColumnName("user_id");

        builder.Property(collaborator => collaborator.CanEdit)
            .HasColumnName("can_edit")
            .IsRequired();

        builder.Property(collaborator => collaborator.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(collaborator => collaborator.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // Naming the same person twice is a duplicate the database refuses, which is what makes the endpoint's
        // `409` a fact rather than a read-then-write guess.
        builder.HasIndex(collaborator => new { collaborator.PlaylistId, collaborator.UserId })
            .IsUnique()
            .HasDatabaseName("ux_playlist_collaborators_playlist_id_user_id");

        builder.HasIndex(collaborator => collaborator.UserId)
            .HasDatabaseName("ix_playlist_collaborators_user_id");

        builder.HasOne<Playlist>()
            .WithMany()
            .HasForeignKey(collaborator => collaborator.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(collaborator => collaborator.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
