using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One account's vote for one suggestion — subject V.2.1.
/// </summary>
/// <remarks>
/// The row **is** the vote. There is no counter anywhere: a queue's ranking is counted from these rows on every
/// read, because a stored count is a second copy of a fact two writers can disagree about, and incrementing one
/// is exactly the lost update subject V.2.1 grades — SPEC-018 decision 2.
/// <para>
/// <see cref="EventId"/> is carried alongside <see cref="SuggestionId"/>, which implies it: the unique index
/// and the queue query both read it, and SDD §8.2 names the index over all three columns.
/// </para>
/// </remarks>
public sealed class EventVote : Entity
{
    /// <summary>The event.</summary>
    public Guid EventId { get; set; }

    /// <summary>The suggestion voted for.</summary>
    public Guid SuggestionId { get; set; }

    /// <summary>Who voted. From the validated token, never from the request body.</summary>
    public Guid UserId { get; set; }
}
