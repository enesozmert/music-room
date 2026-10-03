using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One searched query and the video ids it returned — ADR-0023, SPEC-025 decision 3.
/// </summary>
/// <remarks>
/// This table exists for one reason: `search.list` costs about a hundred times a `videos.list`, so a repeated
/// search must not reach the provider again. ADR-0023 requires the query-to-ids mapping to be cached, and an
/// in-memory-only mapping burns the quota again after every restart — which is the failure the ADR rejects, and
/// restarts cluster right before a demo.
/// <para>
/// The ids are kept, not the metadata: each id resolves through <see cref="TrackRef"/>, so one video's title is
/// stored once however many searches found it.
/// </para>
/// </remarks>
public sealed class TrackSearchCache : Entity
{
    /// <summary>How long a mapping is used before the search runs again.</summary>
    /// <remarks>
    /// Six hours, against thirty days for a <see cref="TrackRef"/>: a title does not change, but a search for
    /// one should find a video uploaded this morning.
    /// </remarks>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(6);

    /// <summary>
    /// The normalised query — trimmed, lower-cased, inner whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// Normalised rather than hashed: a hash would hide what the cache holds from anyone reading the table, and
    /// the value is a search term the caller typed, not a secret. Normalising is what makes "Daft  Punk" and
    /// "daft punk" one row instead of two provider calls.
    /// </remarks>
    public required string Query { get; set; }

    /// <summary>The ids the provider returned, in the order it ranked them.</summary>
    public required IReadOnlyList<string> VideoIds { get; set; }

    /// <summary>When the provider last answered for this query.</summary>
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>Whether this mapping is old enough to search again.</summary>
    /// <param name="nowUtc">The current time, from the caller's clock.</param>
    /// <param name="lifetime">How long a mapping stays usable.</param>
    /// <returns>True when the search should be re-issued.</returns>
    public bool IsStale(DateTime nowUtc, TimeSpan lifetime) => nowUtc - FetchedAtUtc >= lifetime;

    /// <summary>
    /// Normalises a query the way a stored row is normalised.
    /// </summary>
    /// <param name="query">What the caller typed.</param>
    /// <returns>The lookup key.</returns>
    public static string Normalise(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries)).ToLowerInvariant();
    }
}
