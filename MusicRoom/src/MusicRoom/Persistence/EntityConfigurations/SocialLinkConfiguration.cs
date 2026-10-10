using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="SocialLink"/> to <c>social_links</c> — SPEC-041.
/// </summary>
internal sealed class SocialLinkConfiguration : IEntityTypeConfiguration<SocialLink>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<SocialLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // "google", "facebook". A configuration row's name, not a free-text field.
        const int providerMaximumLength = 32;

        // Google's `sub` is 21 digits today and documented as opaque; Facebook's is of similar order.
        // 128 is room for a provider that lengthens its identifier without a migration.
        const int providerUserIdMaximumLength = 128;

        const int emailMaximumLength = 320;

        builder.ToTable("social_links");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.Id).HasColumnName("id");
        builder.Property(link => link.UserId).HasColumnName("user_id");

        builder.Property(link => link.Provider)
            .HasColumnName("provider")
            .HasMaxLength(providerMaximumLength)
            .IsRequired();

        builder.Property(link => link.ProviderUserId)
            .HasColumnName("provider_user_id")
            .HasMaxLength(providerUserIdMaximumLength)
            .IsRequired();

        // A label, not a key: nothing looks an account up by it — ADR-0008's account-takeover note.
        builder.Property(link => link.Email)
            .HasColumnName("email")
            .HasMaxLength(emailMaximumLength);

        builder.Property(link => link.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(link => link.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // The identity model, enforced by the database rather than by a read-then-write check: one Google
        // account signs in to one MusicRoom account, and two simultaneous links of the same identity are
        // arbitrated here. Same reasoning as `ux_users_email`.
        builder.HasIndex(link => new { link.Provider, link.ProviderUserId })
            .IsUnique()
            .HasDatabaseName("ux_social_links_provider_provider_user_id");

        // One link per provider per account: a second Google identity on the same account would make
        // "unlink google" ambiguous.
        builder.HasIndex(link => new { link.UserId, link.Provider })
            .IsUnique()
            .HasDatabaseName("ux_social_links_user_id_provider");

        // Deleting an account takes its links with it: a link that outlived its user signs nobody in.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(link => link.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
