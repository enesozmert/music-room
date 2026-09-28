using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="AuditRecord"/> to <c>audit_records</c>.
/// </summary>
internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    // Internal rather than private because the .editorconfig naming rule requires an underscore prefix on
    // private fields, and a const is a field to that rule.
    internal const int ShortTextLength = 64;

    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_records");
        builder.HasKey(record => record.Id);

        MapIdentityColumns(builder);
        MapClientColumns(builder);
        MapTimestampColumns(builder);
        MapIndexes(builder);
    }

    /// <summary>The action, its target, and who acted.</summary>
    private static void MapIdentityColumns(EntityTypeBuilder<AuditRecord> builder)
    {
        const int actionLength = 128;

        builder.Property(record => record.Id).HasColumnName("id");
        builder.Property(record => record.UserId).HasColumnName("user_id");
        builder.Property(record => record.TargetId).HasColumnName("target_id");

        builder.Property(record => record.Action)
            .HasColumnName("action")
            .HasMaxLength(actionLength)
            .IsRequired();

        builder.Property(record => record.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(ShortTextLength)
            .IsRequired();
    }

    /// <summary>
    /// The three fields subject V.6 names, plus the trace id and the outcome. Every one of these except the
    /// outcome arrives from a client header, so each is bounded at the column as well as at the middleware.
    /// </summary>
    private static void MapClientColumns(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.Property(record => record.Platform)
            .HasColumnName("platform")
            .HasMaxLength(ShortTextLength)
            .IsRequired();

        builder.Property(record => record.Device)
            .HasColumnName("device")
            .HasMaxLength(ShortTextLength)
            .IsRequired();

        builder.Property(record => record.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(ShortTextLength)
            .IsRequired();

        builder.Property(record => record.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(ShortTextLength)
            .IsRequired();

        builder.Property(record => record.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(ShortTextLength)
            .IsRequired();
    }

    private static void MapTimestampColumns(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.Property(record => record.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(record => record.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(record => record.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");
    }

    private static void MapIndexes(EntityTypeBuilder<AuditRecord> builder)
    {
        // "What did this user do" and "what happened in this window" are the two questions the log exists
        // to answer, so both are indexed.
        builder.HasIndex(record => record.UserId)
            .HasDatabaseName("ix_audit_records_user_id");

        builder.HasIndex(record => record.OccurredAtUtc)
            .HasDatabaseName("ix_audit_records_occurred_at_utc");

        // No foreign key to users, deliberately: a record must outlive the account it describes. Deleting
        // an account must not delete the evidence of what it did — SDD §13.4, "it is evidence".
    }
}
