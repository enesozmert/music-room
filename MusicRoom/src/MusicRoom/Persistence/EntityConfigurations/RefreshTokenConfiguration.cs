using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="RefreshToken"/> to <c>refresh_tokens</c>.
/// </summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);

        MapIdentityColumns(builder);
        MapClientColumns(builder);
        MapTimestampColumns(builder);
        MapIndexesAndKeys(builder);
    }

    private static void MapIdentityColumns(EntityTypeBuilder<RefreshToken> builder)
    {
        // A hex-encoded SHA-256 digest is exactly 64 characters.
        const int tokenHashLength = 64;

        builder.Property(token => token.Id).HasColumnName("id");
        builder.Property(token => token.UserId).HasColumnName("user_id");
        builder.Property(token => token.FamilyId).HasColumnName("family_id");

        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(tokenHashLength)
            .IsFixedLength()
            .IsRequired();
    }

    /// <summary>
    /// The client that started the session, so its owner can recognise it in a session list — SPEC-010.
    /// Bounded at the same 64 characters the middleware enforces on the header.
    /// </summary>
    private static void MapClientColumns(EntityTypeBuilder<RefreshToken> builder)
    {
        const int clientTextLength = 64;
        const int grantedScopeLength = 128;

        builder.Property(token => token.Platform)
            .HasColumnName("platform")
            .HasMaxLength(clientTextLength)
            .IsRequired();

        builder.Property(token => token.Device)
            .HasColumnName("device")
            .HasMaxLength(clientTextLength)
            .IsRequired();

        builder.Property(token => token.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(clientTextLength)
            .IsRequired();

        // The granted scope — SPEC-045. Nullable, and null means a first-party session rather than an empty
        // grant. Bounded at the same 128 characters `authorization_codes.scope` is: the same value is copied
        // from the code that granted it, and a column that could hold more than its source would be a column
        // storing something nothing can produce.
        builder.Property(token => token.Scope)
            .HasColumnName("scope")
            .HasMaxLength(grantedScopeLength);
    }

    private static void MapTimestampColumns(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(token => token.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.ConsumedAtUtc)
            .HasColumnName("consumed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(token => token.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");
    }

    private static void MapIndexesAndKeys(EntityTypeBuilder<RefreshToken> builder)
    {
        // The exchange looks a token up by digest, and a second row with the same digest would mean the
        // generator repeated itself. The index makes that a write failure, not an ambiguous read.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_token_hash");

        // Revoking a family is one statement keyed on this column — SDD §13.1.
        builder.HasIndex(token => token.FamilyId)
            .HasDatabaseName("ix_refresh_tokens_family_id");

        // The foreign key, and the column the session list reads.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        // Deleting an account takes its sessions with it: a refresh token that outlived its user is a row
        // nothing can ever resolve. No navigation property — the token is read by digest or by user id,
        // never traversed from the account.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
