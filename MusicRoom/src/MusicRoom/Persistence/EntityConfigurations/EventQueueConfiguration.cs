using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.EntityConfigurations;

/// <summary>
/// Maps <see cref="EventSuggestion"/> to <c>event_suggestions</c>.
/// </summary>
/// <remarks>
/// Unique per event and video: a second row for the same video would split its votes between two queue
/// entries — SPEC-018 decision 3.
/// </remarks>
internal sealed class EventSuggestionConfiguration : IEntityTypeConfiguration<EventSuggestion>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventSuggestion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_suggestions");
        builder.HasKey(suggestion => suggestion.Id);

        builder.Property(suggestion => suggestion.Id).HasColumnName("id");
        builder.Property(suggestion => suggestion.EventId).HasColumnName("event_id");

        builder.Property(suggestion => suggestion.YoutubeVideoId)
            .HasColumnName("youtube_video_id")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(suggestion => suggestion.SuggestedByUserId)
            .HasColumnName("suggested_by_user_id");

        builder.Property(suggestion => suggestion.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(suggestion => suggestion.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(suggestion => new { suggestion.EventId, suggestion.YoutubeVideoId })
            .IsUnique()
            .HasDatabaseName("ux_event_suggestions_event_id_youtube_video_id");

        builder.HasIndex(suggestion => suggestion.SuggestedByUserId)
            .HasDatabaseName("ix_event_suggestions_suggested_by_user_id");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(suggestion => suggestion.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(suggestion => suggestion.SuggestedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="EventVote"/> to <c>event_votes</c>.
/// </summary>
/// <remarks>
/// <c>ux_event_votes_event_id_suggestion_id_user_id</c> is the arbiter of one vote per account per suggestion —
/// SDD §8.2 names it, and `ef-core-persistence` forbids deciding it with a read-then-write check instead.
/// <para>
/// The cascade from <c>event_suggestions</c> is what makes decision 7 true: removing a suggestion takes its
/// votes with it, so no count can count rows that belong to nothing.
/// </para>
/// </remarks>
internal sealed class EventVoteConfiguration : IEntityTypeConfiguration<EventVote>
{
    /// <summary>Declares the mapping.</summary>
    /// <param name="builder">The entity being configured.</param>
    public void Configure(EntityTypeBuilder<EventVote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_votes");
        builder.HasKey(vote => vote.Id);

        builder.Property(vote => vote.Id).HasColumnName("id");
        builder.Property(vote => vote.EventId).HasColumnName("event_id");
        builder.Property(vote => vote.SuggestionId).HasColumnName("suggestion_id");
        builder.Property(vote => vote.UserId).HasColumnName("user_id");

        builder.Property(vote => vote.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(vote => vote.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(vote => new { vote.EventId, vote.SuggestionId, vote.UserId })
            .IsUnique()
            .HasDatabaseName("ux_event_votes_event_id_suggestion_id_user_id");

        // The queue counts votes per suggestion, which this index answers without reading the table.
        builder.HasIndex(vote => vote.SuggestionId)
            .HasDatabaseName("ix_event_votes_suggestion_id");

        builder.HasIndex(vote => vote.UserId).HasDatabaseName("ix_event_votes_user_id");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(vote => vote.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EventSuggestion>()
            .WithMany()
            .HasForeignKey(vote => vote.SuggestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(vote => vote.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
