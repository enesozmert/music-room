using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="EmailVerificationToken"/> to <c>email_verification_tokens</c>.
/// </summary>
internal sealed class EmailVerificationTokenConfiguration
    : IEntityTypeConfiguration<EmailVerificationToken>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A hex-encoded SHA-256 digest is exactly 64 characters.
        const int tokenHashLength = 64;

        builder.ToTable("email_verification_tokens");
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

        // A second row with the same digest would mean the generator repeated itself. The index
        // makes that a write failure rather than an ambiguous lookup.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_email_verification_tokens_token_hash");

        // The lookup the verification endpoint will do, and the foreign key, are the same column.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_email_verification_tokens_user_id");

        // Deleting an account takes its outstanding tokens with it: a token that outlived its user
        // is a row nothing can ever resolve. No navigation property — the token is read by user id,
        // never traversed from the account.
        builder.HasOne<Domain.Entities.User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
