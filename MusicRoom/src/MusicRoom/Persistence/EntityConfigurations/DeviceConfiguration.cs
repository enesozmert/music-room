using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="Device"/> to <c>devices</c>.
/// </summary>
internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("devices");
        builder.HasKey(device => device.Id);

        MapColumns(builder);
        MapIndexesAndKeys(builder);
    }

    private static void MapColumns(EntityTypeBuilder<Device> builder)
    {
        // The same width the audit columns use, because these values come from the same three headers.
        const int clientValueLength = 64;

        builder.Property(device => device.Id).HasColumnName("id");
        builder.Property(device => device.OwnerId).HasColumnName("owner_id");

        builder.Property(device => device.Platform)
            .HasColumnName("platform")
            .HasMaxLength(clientValueLength)
            .IsRequired();

        builder.Property(device => device.Model)
            .HasColumnName("model")
            .HasMaxLength(clientValueLength)
            .IsRequired();

        builder.Property(device => device.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(clientValueLength)
            .IsRequired();

        builder.Property(device => device.ControllerUserId).HasColumnName("controller_user_id");

        builder.Property(device => device.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(device => device.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // PostgreSQL's own row version, as the playlist and event aggregates use it. This is the token that
        // makes two simultaneous control claims resolve to one `200` and one `412` — SPEC-020 decision 5.
        builder.Property(device => device.Version)
            .HasColumnName("xmin")
            .IsRowVersion();
    }

    private static void MapIndexesAndKeys(EntityTypeBuilder<Device> builder)
    {
        // One row per owner, platform and model. `app_version` is deliberately absent: a phone that updates its
        // app is the same phone, and including it would register a new device per release.
        builder.HasIndex(device => new { device.OwnerId, device.Platform, device.Model })
            .IsUnique()
            .HasDatabaseName("ux_devices_owner_id_platform_model");

        // `GET /me/control-grants` reads this.
        builder.HasIndex(device => device.ControllerUserId)
            .HasDatabaseName("ix_devices_controller_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(device => device.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // A deleted account stops controlling anybody else's device, and the device survives: the grant was
        // theirs, the hardware was not.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(device => device.ControllerUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
