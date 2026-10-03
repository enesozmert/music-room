using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using MusicRoom.Application.Abstractions.Catalog;

namespace MusicRoom.Infrastructure.Catalog;

/// <summary>
/// YouTube Data API v3, and nothing else — ADR-0003.
/// </summary>
/// <remarks>
/// The only type in this solution that speaks to the provider. No cache here on purpose: this class spends
/// quota every time it is called, and <see cref="CachedCatalog"/> is what decides whether it is called at all.
/// <para>
/// **Quota is logged in units**, because `youtube-integration` asks for the budget to be measurable before it is
/// exhausted, and a call count without its cost does not say how much of the day is left: `search.list` is 100
/// units, `videos.list` is 1.
/// </para>
/// <para>
/// `403 quotaExceeded` and a provider `5xx` both become <see cref="CatalogOutcome.Unavailable"/>, which the
/// route answers `503` for. Neither is an exception and neither is a `500`: the caller's request was fine.
/// </para>
/// </remarks>
internal sealed class YouTubeCatalogProvider : ICatalogProvider
{
    /// <summary>What one `search.list` costs against the daily quota.</summary>
    internal const int SearchQuotaUnits = 100;

    /// <summary>What one `videos.list` costs, however many ids it resolves.</summary>
    internal const int VideoListQuotaUnits = 1;

    private readonly HttpClient _client;
    private readonly ILogger<YouTubeCatalogProvider> _logger;
    private readonly CatalogOptions _options;

    /// <summary>Takes the typed client, a logger and the key.</summary>
    /// <param name="client">The typed client, carrying the base address and the timeout.</param>
    /// <param name="logger">The application logger.</param>
    /// <param name="options">The configured key and lifetimes.</param>
    /// <remarks>
    /// Public on an internal type: the container activates the registered implementation through a public
    /// constructor only.
    /// </remarks>
    public YouTubeCatalogProvider(
        HttpClient client,
        ILogger<YouTubeCatalogProvider> logger,
        CatalogOptions options)
    {
        _client = client;
        _logger = logger;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<CatalogResult<IReadOnlyList<string>>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        if (!_options.HasProvider)
        {
            return CatalogResult<IReadOnlyList<string>>.Unavailable();
        }

        string path = "youtube/v3/search?part=id&type=video&maxResults="
            + maximumResults.ToString(CultureInfo.InvariantCulture)
            + "&q=" + Uri.EscapeDataString(query)
            + "&key=" + Uri.EscapeDataString(_options.ApiKey!);

        SearchListResponse? answer = await ReadAsync<SearchListResponse>(
            path,
            "search.list",
            SearchQuotaUnits,
            cancellationToken).ConfigureAwait(false);

        if (answer is null)
        {
            return CatalogResult<IReadOnlyList<string>>.Unavailable();
        }

        IReadOnlyList<string> ids =
        [
            .. (answer.Items ?? [])
                .Select(item => item.Id?.VideoId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!),
        ];

        return CatalogResult<IReadOnlyList<string>>.Found(ids);
    }

    /// <inheritdoc />
    public async Task<CatalogResult<IReadOnlyList<TrackMetadata>>> GetAsync(
        IReadOnlyList<string> youtubeVideoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(youtubeVideoIds);

        if (youtubeVideoIds.Count == 0)
        {
            return CatalogResult<IReadOnlyList<TrackMetadata>>.Found([]);
        }

        if (!_options.HasProvider)
        {
            return CatalogResult<IReadOnlyList<TrackMetadata>>.Unavailable();
        }

        // One call for every id, which is the whole reason `videos.list` is preferred: fifty ids cost the same
        // single unit as one.
        string path = "youtube/v3/videos?part=snippet,contentDetails&id="
            + Uri.EscapeDataString(string.Join(',', youtubeVideoIds))
            + "&key=" + Uri.EscapeDataString(_options.ApiKey!);

        VideoListResponse? answer = await ReadAsync<VideoListResponse>(
            path,
            "videos.list",
            VideoListQuotaUnits,
            cancellationToken).ConfigureAwait(false);

        if (answer is null)
        {
            return CatalogResult<IReadOnlyList<TrackMetadata>>.Unavailable();
        }

        IReadOnlyList<TrackMetadata> tracks =
        [
            .. (answer.Items ?? [])
                .Select(ToMetadata)
                .Where(track => track is not null)
                .Select(track => track!),
        ];

        return CatalogResult<IReadOnlyList<TrackMetadata>>.Found(tracks);
    }

    /// <summary>
    /// Reads one provider call, or returns null when the catalog is unavailable.
    /// </summary>
    /// <remarks>
    /// The provider's error body is never logged and never propagated — the status code is the whole of what a
    /// reader needs, and an error document may contain our own request echoed back (API10). The key is in the
    /// query string the provider requires, which is why no part of the request URI is logged either.
    /// </remarks>
    private async Task<TResponse?> ReadAsync<TResponse>(
        string path,
        string call,
        int quotaUnits,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(path, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "YouTube {CatalogCall} answered {StatusCode}, costing {QuotaUnits} quota units.",
                call,
                (int)response.StatusCode,
                quotaUnits);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // The quota is the overwhelmingly likely reason for a 403 on a key-authenticated read, and it is
                // the one the standard asks to handle as a first-class state.
                _logger.LogWarning(
                    "YouTube refused {CatalogCall} with 403. The daily quota is the usual cause.",
                    call);

                return null;
            }

            return response.IsSuccessStatusCode
                ? await response.Content
                    .ReadFromJsonAsync<TResponse>(cancellationToken)
                    .ConfigureAwait(false)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            // A timeout, a DNS failure, or the circuit breaker being open. All of them mean the same thing to a
            // caller, and none of them is the caller's fault.
            _logger.LogWarning(exception, "YouTube {CatalogCall} could not be completed.", call);

            return null;
        }
    }

    /// <summary>
    /// Normalises one provider item, or returns null when it is missing what a `TrackRef` needs.
    /// </summary>
    private static TrackMetadata? ToMetadata(VideoItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || item.Snippet is null)
        {
            return null;
        }

        return new TrackMetadata(
            item.Id,
            item.Snippet.Title ?? string.Empty,
            item.Snippet.ChannelTitle ?? string.Empty,
            DurationSeconds(item.ContentDetails?.Duration),
            Thumbnail(item.Snippet));
    }

    /// <summary>
    /// The medium thumbnail, the default one, or nothing.
    /// </summary>
    /// <remarks>
    /// Its own method because the null checks on the way to a URL are enough branching to put
    /// <see cref="ToMetadata"/> over the complexity gate on their own.
    /// </remarks>
    private static string Thumbnail(VideoSnippet snippet) =>
        snippet.Thumbnails?.Medium?.Url ?? snippet.Thumbnails?.Default?.Url ?? string.Empty;

    /// <summary>
    /// Reads YouTube's ISO 8601 duration, or zero.
    /// </summary>
    /// <remarks>
    /// Zero means "no duration", which is what a live stream has: the provider omits the field for one. A live
    /// stream is still a legitimate track to queue, so it is not rejected here.
    /// </remarks>
    private static int DurationSeconds(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
        {
            return 0;
        }

        try
        {
            return (int)XmlConvert.ToTimeSpan(isoDuration).TotalSeconds;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}
