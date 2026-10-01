using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="Friendship"/> to <c>friendships</c>, one row per friendship.
/// </summary>
internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("friendships");
        builder.HasKey(friendship => friendship.Id);

        builder.Property(friendship => friendship.Id).HasColumnName("id");
        builder.Property(friendship => friendship.UserAId).HasColumnName("user_a_id");
        builder.Property(friendship => friendship.UserBId).HasColumnName("user_b_id");

        builder.Property(friendship => friendship.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(friendship => friendship.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // The pair is stored in canonical order, so this index is what makes a duplicate friendship impossible
        // — including the one two crossing accepts would otherwise create.
        builder.HasIndex(friendship => new { friendship.UserAId, friendship.UserBId })
            .IsUnique()
            .HasDatabaseName("ux_friendships_user_a_id_user_b_id");

        // A friend list reads either column, so the second one is indexed too.
        builder.HasIndex(friendship => friendship.UserBId)
            .HasDatabaseName("ix_friendships_user_b_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(friendship => friendship.UserAId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(friendship => friendship.UserBId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
