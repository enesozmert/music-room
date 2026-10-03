namespace MusicRoom.Application.Abstractions.Catalog;

/// <summary>
/// One track, as every layer above <c>Infrastructure</c> sees it — `docs/api/ENDPOINTS.md` §3.
/// </summary>
/// <param name="YoutubeVideoId">The 11-character video id.</param>
/// <param name="Title">The video's title.</param>
/// <param name="Channel">The channel that published it.</param>
/// <param name="DurationSeconds">How long it runs; zero for a live stream.</param>
/// <param name="ThumbnailUrl">A thumbnail URL, served by the provider and never proxied.</param>
/// <remarks>
/// The normalised shape, not the provider's payload. ADR-0023 is explicit that only this is cached, and the
/// subject's warning — *the SDK must not do your work for you* — is why nothing above this layer ever sees a
/// YouTube response.
/// </remarks>
public sealed record TrackMetadata(
    string YoutubeVideoId,
    string Title,
    string Channel,
    int DurationSeconds,
    string ThumbnailUrl);

/// <summary>
/// What a catalog read answered.
/// </summary>
/// <typeparam name="TValue">What was asked for: one track, or a page of them.</typeparam>
/// <param name="Outcome">Whether the value is there, absent, or unavailable.</param>
/// <param name="Value">The value, when the outcome is <see cref="CatalogOutcome.Found"/>.</param>
/// <remarks>
/// A result rather than an exception for the unavailable case, because "the catalog is down" is an answer this
/// API gives on purpose — `503` with its own problem type (SPEC-025 decision 5). An exception would make the
/// expected path look like a fault and would reach a caller as `500`.
/// </remarks>
public sealed record CatalogResult<TValue>(CatalogOutcome Outcome, TValue? Value)
    where TValue : class
{
    /// <summary>The provider or a cache answered.</summary>
    /// <param name="value">What it answered with.</param>
    /// <returns>A found result.</returns>
    public static CatalogResult<TValue> Found(TValue value) => new(CatalogOutcome.Found, value);

    /// <summary>The catalog has no such video.</summary>
    /// <returns>A missing result.</returns>
    public static CatalogResult<TValue> Missing() => new(CatalogOutcome.Missing, null);

    /// <summary>The provider could not be reached, or the quota is spent.</summary>
    /// <returns>An unavailable result.</returns>
    public static CatalogResult<TValue> Unavailable() => new(CatalogOutcome.Unavailable, null);
}

/// <summary>
/// The three answers a catalog read can give.
/// </summary>
public enum CatalogOutcome
{
    /// <summary>The value is here.</summary>
    Found = 0,

    /// <summary>The catalog does not have it — `404`.</summary>
    Missing = 1,

    /// <summary>
    /// The provider is unreachable or the daily quota is spent — `503`, never `500`.
    /// </summary>
    /// <remarks>
    /// One outcome for both, because the distinction a caller can act on is the same: the request was fine, the
    /// catalog is not available, try later. Which of the two it was belongs in the log.
    /// </remarks>
    Unavailable = 2,
}
