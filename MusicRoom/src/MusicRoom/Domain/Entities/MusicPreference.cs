using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One music preference of one account — a genre or an artist the user named. Subject V.1.
/// </summary>
/// <remarks>
/// Normalised rather than a document, so a future recommender can query it (SPEC-011 escalation 3). It carries
/// **no visibility tier**: preferences are private to their owner today, because nothing reads them and
/// exposing data with no consumer is a decision with no benefit. Adding a tier later is additive.
/// <para>
/// Issue O14 stays true: V.1 requires storing and updating preferences, and that is all this does.
/// </para>
/// </remarks>
public sealed class MusicPreference : Entity
{
    /// <summary>The account that named it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Whether this is a genre or an artist.</summary>
    public MusicPreferenceKind Kind { get; set; }

    /// <summary>The genre or artist name, as the user typed it.</summary>
    public required string Value { get; set; }
}

/// <summary>
/// The two kinds of preference the contract's `PUT /me/music-preferences` accepts — `genres[]` and `artists[]`.
/// </summary>
/// <remarks>
/// Declared beside the entity rather than in <c>Common</c>: it has exactly one owner, unlike
/// <see cref="ProfileFieldVisibility"/> which several aggregates will read.
/// </remarks>
public enum MusicPreferenceKind
{
    /// <summary>A genre, such as "drum and bass".</summary>
    Genre = 0,

    /// <summary>An artist.</summary>
    Artist = 1,
}
