using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="Profile"/> to <c>profiles</c>, one row per account.
/// </summary>
internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("profiles");
        builder.HasKey(profile => profile.Id);

        MapValueColumns(builder);
        MapVisibilityColumns(builder);
        MapTimestampColumns(builder);

        // One profile per account, enforced by the database rather than by a check in a handler.
        builder.HasIndex(profile => profile.UserId)
            .IsUnique()
            .HasDatabaseName("ux_profiles_user_id");

        // Deleting an account takes its profile with it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void MapValueColumns(EntityTypeBuilder<Profile> builder)
    {
        const int realNameLength = 128;
        const int bioLength = 512;
        const int locationLength = 128;

        builder.Property(profile => profile.Id).HasColumnName("id");
        builder.Property(profile => profile.UserId).HasColumnName("user_id");

        builder.Property(profile => profile.RealName)
            .HasColumnName("real_name")
            .HasMaxLength(realNameLength);

        builder.Property(profile => profile.Bio)
            .HasColumnName("bio")
            .HasMaxLength(bioLength);

        builder.Property(profile => profile.Location)
            .HasColumnName("location")
            .HasMaxLength(locationLength);

        builder.Property(profile => profile.BirthDate)
            .HasColumnName("birth_date")
            .HasColumnType("date");
    }

    /// <summary>
    /// The tiers, stored as <c>smallint</c>. An enum persisted as text is a value nothing validates, and these
    /// three decide who may read a field.
    /// </summary>
    private static void MapVisibilityColumns(EntityTypeBuilder<Profile> builder)
    {
        builder.Property(profile => profile.RealNameVisibility)
            .HasColumnName("real_name_visibility")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(profile => profile.BioVisibility)
            .HasColumnName("bio_visibility")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(profile => profile.LocationVisibility)
            .HasColumnName("location_visibility")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(profile => profile.BirthDateVisibility)
            .HasColumnName("birth_date_visibility")
            .HasColumnType("smallint")
            .IsRequired();
    }

    private static void MapTimestampColumns(EntityTypeBuilder<Profile> builder)
    {
        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(profile => profile.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");
    }
}
