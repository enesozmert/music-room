using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One track's metadata, as the only thing this system keeps about a video — ADR-0003, ADR-0023.
/// </summary>
/// <remarks>
/// Both the persistent cache (L2) and the reference data, which ADR-0023 points out are the same rows: playlist
/// and event rows already carry a video id with no title beside it, so these rows would be needed even if the
/// provider were never called again.
/// <para>
/// **No media, ever.** Video id, title, channel, duration and a thumbnail URL. The subject makes re-hosting a
/// failing condition and YouTube's terms forbid it; what plays, plays on the client through the player SDK.
/// </para>
/// <para>
/// <see cref="FetchedAtUtc"/> is what makes the row refresh by age. ADR-0023 chose age over invalidation: track
/// metadata barely changes, and a stale thumbnail is not a defect worth two invalidation paths.
/// </para>
/// </remarks>
public sealed class TrackRef : Entity
{
    /// <summary>How long a reference is used before it is fetched again.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    /// <summary>The 11-character YouTube video id. Unique: one row per video.</summary>
    public required string YoutubeVideoId { get; set; }

    /// <summary>The video's title, as the provider gave it.</summary>
    public required string Title { get; set; }

    /// <summary>The channel that published it.</summary>
    public required string Channel { get; set; }

    /// <summary>How long it runs. Zero when the provider reports a live stream, which has no duration.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>A thumbnail URL. The image is served by the provider and never proxied.</summary>
    public required string ThumbnailUrl { get; set; }

    /// <summary>When the provider last answered for this video.</summary>
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>Whether this row is old enough to fetch again.</summary>
    /// <param name="nowUtc">The current time, from the caller's clock.</param>
    /// <param name="lifetime">How long a row stays usable.</param>
    /// <returns>True when the row should be refreshed.</returns>
    public bool IsStale(DateTime nowUtc, TimeSpan lifetime) => nowUtc - FetchedAtUtc >= lifetime;
}
