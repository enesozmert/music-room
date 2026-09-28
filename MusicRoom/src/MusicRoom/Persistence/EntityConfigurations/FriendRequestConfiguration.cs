using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="FriendRequest"/> to <c>friend_requests</c>.
/// </summary>
internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("friend_requests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id).HasColumnName("id");
        builder.Property(request => request.SenderId).HasColumnName("sender_id");
        builder.Property(request => request.RecipientId).HasColumnName("recipient_id");

        builder.Property(request => request.Status)
            .HasColumnName("status")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(request => request.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(request => request.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // One row per ordered pair: asking twice is a duplicate the database refuses rather than a second row
        // the lists would both show. A declined row therefore also blocks an immediate re-ask, which is the
        // point of keeping it.
        builder.HasIndex(request => new { request.SenderId, request.RecipientId })
            .IsUnique()
            .HasDatabaseName("ux_friend_requests_sender_id_recipient_id");

        // The two list queries, one per direction.
        builder.HasIndex(request => request.RecipientId)
            .HasDatabaseName("ix_friend_requests_recipient_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.SenderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both sides cascade: a request that outlived either party is a row nothing can resolve. The second
        // relationship needs no navigation property either.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
