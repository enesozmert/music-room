using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="User"/> to <c>users</c>.
/// </summary>
/// <remarks>
/// Every table and column name is written out. The project pins no naming-convention package, and
/// database-design-migrations asks for explicit names for anything a migration would otherwise
/// rename silently.
/// </remarks>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The RFC 5321 maximum length of an address.
        const int emailMaximumLength = 320;

        // A BCrypt string is 60 characters; the column leaves room for a longer future prefix.
        const int passwordHashMaximumLength = 100;

        const int displayNameMaximumLength = 64;

        builder.ToTable("users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).HasColumnName("id");

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(emailMaximumLength)
            .IsRequired();

        // Nullable: an account that has only ever signed in with a provider has no password — SPEC-041.
        // Not a sentinel string, which the first comparison would treat as a password somebody chose.
        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(passwordHashMaximumLength);

        builder.Property(user => user.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(displayNameMaximumLength)
            .IsRequired();

        builder.Property(user => user.IsEmailVerified).HasColumnName("is_email_verified");

        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        // When the welcome message was sent, or null — SPEC-052. Nullable because "never" is a real state
        // and the column is what makes once mean once.
        builder.Property(user => user.WelcomeSentAtUtc)
            .HasColumnName("welcome_sent_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(user => user.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // The invariant, in the database — SDD §8.2. Registration is a concurrency hotspot, and a
        // read-then-write check in C# would lose the race it is meant to arbitrate.
        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("ux_users_email");
    }
}
