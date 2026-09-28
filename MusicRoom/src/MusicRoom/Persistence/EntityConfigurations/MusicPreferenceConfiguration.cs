using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="MusicPreference"/> to <c>music_preferences</c>.
/// </summary>
internal sealed class MusicPreferenceConfiguration : IEntityTypeConfiguration<MusicPreference>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<MusicPreference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        const int valueLength = 64;

        builder.ToTable("music_preferences");
        builder.HasKey(preference => preference.Id);

        builder.Property(preference => preference.Id).HasColumnName("id");
        builder.Property(preference => preference.UserId).HasColumnName("user_id");

        builder.Property(preference => preference.Kind)
            .HasColumnName("kind")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(preference => preference.Value)
            .HasColumnName("value")
            .HasMaxLength(valueLength)
            .IsRequired();

        builder.Property(preference => preference.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(preference => preference.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // Naming the same genre twice is a duplicate the database refuses, so the handler's de-duplication is a
        // convenience rather than the only guard.
        builder.HasIndex(preference => new { preference.UserId, preference.Kind, preference.Value })
            .IsUnique()
            .HasDatabaseName("ux_music_preferences_user_id_kind_value");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(preference => preference.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
