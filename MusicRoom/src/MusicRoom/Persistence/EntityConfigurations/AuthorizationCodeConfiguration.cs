using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="AuthorizationCode"/> to <c>authorization_codes</c> — SPEC-044.
/// </summary>
internal sealed class AuthorizationCodeConfiguration : IEntityTypeConfiguration<AuthorizationCode>
{
    /// <summary>The width of a client identifier and of a scope list.</summary>
    private const int _nameLength = 128;

    /// <summary>The width of a redirect URI, which is the widest column here.</summary>
    private const int _redirectUriLength = 512;

    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<AuthorizationCode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("authorization_codes");
        builder.HasKey(code => code.Id);

        Columns(builder);
        Instants(builder);
        Indexes(builder);
    }

    /// <summary>The columns that carry the binding.</summary>
    /// <remarks>
    /// Three methods rather than one, because the whole mapping is 50 lines and the root .editorconfig caps
    /// a method at 40 (S138). The split follows what each group is: the binding, the instants, the indexes.
    /// </remarks>
    private static void Columns(EntityTypeBuilder<AuthorizationCode> builder)
    {
        builder.Property(code => code.Id).HasColumnName("id");
        builder.Property(code => code.UserId).HasColumnName("user_id");

        builder.Property(code => code.ClientId)
            .HasColumnName("client_id")
            .HasMaxLength(_nameLength)
            .IsRequired();

        builder.Property(code => code.RedirectUri)
            .HasColumnName("redirect_uri")
            .HasMaxLength(_redirectUriLength)
            .IsRequired();

        builder.Property(code => code.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(OpaqueTokenHash.HexLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(code => code.CodeChallenge)
            .HasColumnName("code_challenge")
            .HasMaxLength(PkceChallenge.MaximumVerifierLength)
            .IsRequired();

        builder.Property(code => code.Nonce)
            .HasColumnName("nonce")
            .HasMaxLength(_nameLength);

        builder.Property(code => code.Scope)
            .HasColumnName("scope")
            .HasMaxLength(_nameLength)
            .IsRequired();

    }

    /// <summary>The four instants, all stored with a time zone — SDD §8.3.</summary>
    private static void Instants(EntityTypeBuilder<AuthorizationCode> builder)
    {
        builder.Property(code => code.AuthenticatedAtUtc)
            .HasColumnName("authenticated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(code => code.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(code => code.ConsumedAtUtc)
            .HasColumnName("consumed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(code => code.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(code => code.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

    }

    /// <summary>The unique digest, and the account the row belongs to.</summary>
    private static void Indexes(EntityTypeBuilder<AuthorizationCode> builder)
    {
        // A second row with the same digest would mean the generator repeated itself. The index makes that a
        // write failure rather than an ambiguous lookup — the same guard the verification token carries.
        builder.HasIndex(code => code.CodeHash)
            .IsUnique()
            .HasDatabaseName("ux_authorization_codes_code_hash");

        // The foreign key's own index, named rather than left to EF: the convention this schema holds
        // itself to is lower-case `ix_`, and `SchemaConventionTests` fails a default `IX_` name.
        builder.HasIndex(code => code.UserId)
            .HasDatabaseName("ix_authorization_codes_user_id");

        // Deleting an account takes its outstanding codes with it: a code that outlived its user is a row
        // nothing can ever resolve. No navigation property — a code is read by its digest, never traversed.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
