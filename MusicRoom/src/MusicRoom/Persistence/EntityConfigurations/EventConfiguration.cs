using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Application.Features.Events;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="Event"/> to <c>events</c>.
/// </summary>
/// <remarks>
/// The four policy columns are nullable: the two other licence modes have no policy, and a row of defaults
/// would read as a geofence at the equator rather than as no geofence — SPEC-017 decision 1. A
/// `LocationAndTimeRestricted` event with no policy is refused by the validator, so the combination never
/// reaches a row.
/// </remarks>
internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("events");
        builder.HasKey(candidate => candidate.Id);

        MapColumns(builder);
        MapPolicy(builder);

        // Soft delete, applied by EF to every query on this type — the same mechanism SPEC-014 gave playlists.
        builder.HasQueryFilter(candidate => candidate.DeletedAtUtc == null);

        builder.HasIndex(candidate => candidate.OwnerId).HasDatabaseName("ix_events_owner_id");

        // The public list is ordered by when an event starts, so the scope query reads an index instead of
        // sorting every public event in the database.
        builder.HasIndex(candidate => candidate.StartsAtUtc).HasDatabaseName("ix_events_starts_at_utc");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(candidate => candidate.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void MapColumns(EntityTypeBuilder<Event> builder)
    {
        builder.Property(candidate => candidate.Id).HasColumnName("id");
        builder.Property(candidate => candidate.OwnerId).HasColumnName("owner_id");

        builder.Property(candidate => candidate.Name)
            .HasColumnName("name")
            .HasMaxLength(EventLicenceRules.MaximumNameLength)
            .IsRequired();

        builder.Property(candidate => candidate.Visibility)
            .HasColumnName("visibility")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(candidate => candidate.Licence)
            .HasColumnName("licence")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(candidate => candidate.StartsAtUtc)
            .HasColumnName("starts_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(candidate => candidate.EndsAtUtc)
            .HasColumnName("ends_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(candidate => candidate.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(candidate => candidate.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(candidate => candidate.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // PostgreSQL's own row version as the concurrency token — SDD §12.2, the same mechanism the playlist
        // aggregate uses. No column is created and nothing in the application increments it.
        builder.Property(candidate => candidate.Version)
            .HasColumnName("xmin")
            .IsRowVersion();
    }

    private static void MapPolicy(EntityTypeBuilder<Event> builder)
    {
        builder.Property(candidate => candidate.PresenceProof)
            .HasColumnName("presence_proof")
            .HasColumnType("smallint");

        // Six decimal places: about 0.1 m, which is what makes a radius check mean anything. These are the
        // **venue's** coordinates, the event's own published fact — a voter's position is checked and
        // discarded and never stored (ADR-0014, SPEC-017 decision 6).
        builder.Property(candidate => candidate.GeofenceLatitude)
            .HasColumnName("geofence_latitude")
            .HasColumnType("numeric(9,6)");

        builder.Property(candidate => candidate.GeofenceLongitude)
            .HasColumnName("geofence_longitude")
            .HasColumnType("numeric(9,6)");

        builder.Property(candidate => candidate.GeofenceRadiusMetres)
            .HasColumnName("geofence_radius_metres");
    }
}
