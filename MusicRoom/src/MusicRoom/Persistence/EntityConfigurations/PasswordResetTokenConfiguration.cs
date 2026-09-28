using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="PasswordResetToken"/> to <c>password_reset_tokens</c>.
/// </summary>
internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A hex-encoded SHA-256 digest is exactly 64 characters.
        const int tokenHashLength = 64;

        builder.ToTable("password_reset_tokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id).HasColumnName("id");
        builder.Property(token => token.UserId).HasColumnName("user_id");

        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(tokenHashLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(token => token.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.ConsumedAtUtc)
            .HasColumnName("consumed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        // The reset endpoint looks a token up by digest, and a repeated digest would mean the generator
        // repeated itself. The index makes that a write failure rather than an ambiguous read.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_password_reset_tokens_token_hash");

        // Superseding an account's outstanding tokens is one statement keyed on this column.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_password_reset_tokens_user_id");

        // Deleting an account takes its outstanding reset tokens with it. No navigation property — the
        // token is read by digest or by user id, never traversed from the account.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
