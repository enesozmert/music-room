using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One track suggested for an event — subject V.2.1.
/// </summary>
/// <remarks>
/// Unique per event and video, enforced by the database. A second row for the same video would split its votes
/// between two queue entries, which is worse than refusing the duplicate — SPEC-018 decision 3. That is the
/// opposite of a playlist, where the same track twice is a playlist that plays it twice.
/// <para>
/// The video id is stored as the client sent it. Resolving it to a title or a duration is slice S4's catalog
/// (ADR-0023) and is not this entity's business.
/// </para>
/// </remarks>
public sealed class EventSuggestion : Entity
{
    /// <summary>The event.</summary>
    public Guid EventId { get; set; }

    /// <summary>The YouTube video id, verbatim.</summary>
    public required string YoutubeVideoId { get; set; }

    /// <summary>Who suggested it. They, and the event's owner, may remove it.</summary>
    public Guid SuggestedByUserId { get; set; }
}
