using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="EventInvitation"/> to <c>event_invitations</c>.
/// </summary>
/// <remarks>
/// The unique index is the gate's integrity: inviting the same person twice is a duplicate the database
/// refuses, which is what makes the endpoint's `409` a fact rather than a read-then-write guess.
/// </remarks>
internal sealed class EventInvitationConfiguration : IEntityTypeConfiguration<EventInvitation>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_invitations");
        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id).HasColumnName("id");
        builder.Property(invitation => invitation.EventId).HasColumnName("event_id");
        builder.Property(invitation => invitation.UserId).HasColumnName("user_id");

        builder.Property(invitation => invitation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(invitation => invitation.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(invitation => new { invitation.EventId, invitation.UserId })
            .IsUnique()
            .HasDatabaseName("ux_event_invitations_event_id_user_id");

        // The `invited` scope lists one caller's invitations, so it reads this index rather than the table.
        builder.HasIndex(invitation => invitation.UserId)
            .HasDatabaseName("ix_event_invitations_user_id");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(invitation => invitation.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invitation => invitation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="EventAttendance"/> to <c>event_attendances</c>.
/// </summary>
/// <remarks>
/// Beside the invitation configuration because the two tables are the same shape and are read together; both
/// stay under the file-length gate this way. One row per person per event, enforced by the database.
/// </remarks>
internal sealed class EventAttendanceConfiguration : IEntityTypeConfiguration<EventAttendance>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventAttendance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_attendances");
        builder.HasKey(attendance => attendance.Id);

        builder.Property(attendance => attendance.Id).HasColumnName("id");
        builder.Property(attendance => attendance.EventId).HasColumnName("event_id");
        builder.Property(attendance => attendance.UserId).HasColumnName("user_id");

        builder.Property(attendance => attendance.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(attendance => attendance.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(attendance => new { attendance.EventId, attendance.UserId })
            .IsUnique()
            .HasDatabaseName("ux_event_attendances_event_id_user_id");

        builder.HasIndex(attendance => attendance.UserId)
            .HasDatabaseName("ix_event_attendances_user_id");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(attendance => attendance.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(attendance => attendance.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
