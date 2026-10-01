using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="EventCode"/> to <c>event_codes</c>.
/// </summary>
/// <remarks>
/// Only the digest is stored, and the unique index on it is what makes the spend a single indexed read. There is
/// no plaintext column: a leaked database must not let anybody vote — SPEC-018 decision 4.
/// </remarks>
internal sealed class EventCodeConfiguration : IEntityTypeConfiguration<EventCode>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventCode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_codes");
        builder.HasKey(code => code.Id);

        builder.Property(code => code.Id).HasColumnName("id");
        builder.Property(code => code.EventId).HasColumnName("event_id");

        builder.Property(code => code.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(OpaqueTokenHash.HexLength)
            .IsRequired();

        builder.Property(code => code.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(code => code.IssuedByUserId).HasColumnName("issued_by_user_id");

        builder.Property(code => code.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(code => code.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(code => code.CodeHash)
            .IsUnique()
            .HasDatabaseName("ux_event_codes_code_hash");

        builder.HasIndex(code => code.EventId).HasDatabaseName("ix_event_codes_event_id");

        // Declared rather than left to EF's own foreign-key index, whose PascalCase name the schema convention
        // tests reject.
        builder.HasIndex(code => code.IssuedByUserId)
            .HasDatabaseName("ix_event_codes_issued_by_user_id");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(code => code.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(code => code.IssuedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="EventCodeRedemption"/> to <c>event_code_redemptions</c>.
/// </summary>
/// <remarks>
/// The unique index is what makes "single use per account" a fact the database holds: two parallel votes
/// carrying the same code cannot both insert, so the second is refused rather than silently admitted.
/// </remarks>
internal sealed class EventCodeRedemptionConfiguration : IEntityTypeConfiguration<EventCodeRedemption>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventCodeRedemption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_code_redemptions");
        builder.HasKey(redemption => redemption.Id);

        builder.Property(redemption => redemption.Id).HasColumnName("id");
        builder.Property(redemption => redemption.EventCodeId).HasColumnName("event_code_id");
        builder.Property(redemption => redemption.UserId).HasColumnName("user_id");

        builder.Property(redemption => redemption.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(redemption => redemption.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(redemption => new { redemption.EventCodeId, redemption.UserId })
            .IsUnique()
            .HasDatabaseName("ux_event_code_redemptions_event_code_id_user_id");

        builder.HasIndex(redemption => redemption.UserId)
            .HasDatabaseName("ix_event_code_redemptions_user_id");

        builder.HasOne<EventCode>()
            .WithMany()
            .HasForeignKey(redemption => redemption.EventCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(redemption => redemption.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
