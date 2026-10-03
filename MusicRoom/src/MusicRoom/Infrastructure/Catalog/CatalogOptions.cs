namespace MusicRoom.Infrastructure.Catalog;

/// <summary>
/// What the catalog needs from configuration — ADR-0003, ADR-0023.
/// </summary>
/// <param name="ApiKey">The YouTube Data API key, from `.env`. Server-side only: the subject makes a key in a
/// client bundle a failing condition, and it is never logged or returned.</param>
/// <param name="TrackLifetime">How long a stored <c>track_refs</c> row is used before it is fetched again.</param>
/// <param name="SearchLifetime">How long a stored search mapping is used before the search runs again.</param>
/// <param name="MemoryLifetime">How long L1 keeps an entry.</param>
/// <param name="RequestTimeout">How long **one attempt** at a provider call may take.</param>
/// <remarks>
/// The defaults <em>are</em> ADR-0023's policy, so a host with no override still caches the way the decision
/// says. Thirty days for metadata that barely changes, six hours for a search so a video uploaded this morning
/// can be found today, and a short L1 because its only job is to save a database round trip to Frankfurt.
/// </remarks>
public sealed record CatalogOptions(
    string? ApiKey,
    TimeSpan TrackLifetime,
    TimeSpan SearchLifetime,
    TimeSpan MemoryLifetime,
    TimeSpan RequestTimeout)
{
    /// <summary>How long L1 keeps an entry when nothing overrides it.</summary>
    public static readonly TimeSpan DefaultMemoryLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long one attempt may take when nothing overrides it.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The whole budget for a call including its retries.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RequestTimeout"/> because they are different limits, and conflating them cost a
    /// retry: with one 5-second budget for the whole send, the exponential backoff between attempts ate it and
    /// the third attempt never ran. The per-attempt timeout bounds a hung connection; this bounds how long a
    /// caller waits for the catalog to give up.
    /// </remarks>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Whether a provider call is possible at all.</summary>
    /// <remarks>
    /// Without a key the catalog still answers from both caches and reports the provider as unavailable rather
    /// than failing to start. A missing key stops the application from reaching YouTube; it does not stop an
    /// event from running on the tracks it already knows, which is what `youtube-integration` asks for.
    /// </remarks>
    public bool HasProvider => !string.IsNullOrWhiteSpace(ApiKey);
}
