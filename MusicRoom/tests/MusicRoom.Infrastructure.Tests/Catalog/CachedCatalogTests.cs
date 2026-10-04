using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Catalog;

/// <summary>
/// ADR-0023's lookup order, asserted by counting provider calls — which is the only measurement that matters,
/// because a provider call is quota spent.
/// </summary>
/// <remarks>
/// L2 is a recording fake rather than a database: the SQL is `TrackRefRepository`'s business, and what this
/// suite is about is *whether* the provider is reached.
/// </remarks>
public sealed class CachedCatalogTests
{
    internal const string VideoId = "dQw4w9WgXcQ";
    internal const string Query = "never gonna";
    internal const int WorkFactor = 12;

    // Both ids the search body returns. The first version of this fixture resolved only one of them, so the
    // second search correctly went back for the other and the assertion counted it as a cache miss.
    internal const string BothVideosBody = """
        {"items":[
          {"id":"dQw4w9WgXcQ","snippet":{"title":"A song","channelTitle":"A channel",
           "thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg"}}},
           "contentDetails":{"duration":"PT3M33S"}},
          {"id":"oHg5SJYRHA0","snippet":{"title":"Another song","channelTitle":"A channel",
           "thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/oHg5SJYRHA0/mqdefault.jpg"}}},
           "contentDetails":{"duration":"PT0M19S"}}
        ]}
        """;

    /// <summary>A second read of the same video does not reach the provider — L1.</summary>
    [Fact]
    public async Task GetAsync_Twice_CallsTheProviderOnceAsync()
    {
        // Arrange
        (ICatalog catalog, CatalogTransport transport, _) = Build(
            (HttpStatusCode.OK, YouTubeCatalogProviderTests.VideoBody));

        // Act
        await catalog.GetAsync(VideoId, TestContext.Current.CancellationToken);
        CatalogResult<TrackMetadata> second = await catalog.GetAsync(
            VideoId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Found, second.Outcome);
        Assert.Equal(1, transport.CallCount);
    }

    /// <summary>A fresh row in L2 is enough: the provider is not called at all.</summary>
    /// <remarks>
    /// This is the case that makes a restart cheap. ADR-0023 rejected an in-memory-only cache for exactly this,
    /// because restarts cluster right before a demo.
    /// </remarks>
    [Fact]
    public async Task GetAsync_WithAFreshRowInTheDatabase_CallsNothingAsync()
    {
        // Arrange
        RecordingTrackRefs stored = new();
        stored.Put(Track(), DateTime.UtcNow);

        (ICatalog catalog, CatalogTransport transport, _) = Build(stored);

        // Act
        CatalogResult<TrackMetadata> result = await catalog.GetAsync(
            VideoId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Found, result.Outcome);
        Assert.Equal(0, transport.CallCount);
    }

    /// <summary>An aged row is refreshed, and the refreshed value is written back to both levels.</summary>
    [Fact]
    public async Task GetAsync_WithAnAgedRow_RefreshesItAsync()
    {
        // Arrange — older than the configured lifetime.
        RecordingTrackRefs stored = new();
        stored.Put(
            Track() with { Title = "The old title" },
            DateTime.UtcNow - TrackRef.DefaultLifetime - TimeSpan.FromDays(1));

        (ICatalog catalog, CatalogTransport transport, _) = Build(
            stored,
            (HttpStatusCode.OK, YouTubeCatalogProviderTests.VideoBody));

        // Act
        CatalogResult<TrackMetadata> result = await catalog.GetAsync(
            VideoId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("A song", result.Value!.Title);
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(1, stored.Saves);
    }

    /// <summary>
    /// A repeated search re-issues no `search.list` — the quota rule ADR-0023 exists for.
    /// </summary>
    [Fact]
    public async Task SearchAsync_Twice_IssuesOneSearchAsync()
    {
        // Arrange
        (ICatalog catalog, CatalogTransport transport, _) = Build(
            (HttpStatusCode.OK, YouTubeCatalogProviderTests.SearchBody),
            (HttpStatusCode.OK, BothVideosBody));

        // Act
        await catalog.SearchAsync(Query, 1, 10, TestContext.Current.CancellationToken);
        await catalog.SearchAsync(Query, 1, 10, TestContext.Current.CancellationToken);

        // Assert — two calls for the first search (ids, then metadata), and nothing for the second.
        Assert.Equal(2, transport.CallCount);
        Assert.DoesNotContain(
            transport.Requests.Skip(2),
            request => request.Contains("youtube/v3/search", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same query differently cased or spaced is the same cached search, not a second one.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WithADifferentlyCasedQuery_IssuesNoSecondSearchAsync()
    {
        // Arrange
        (ICatalog catalog, CatalogTransport transport, _) = Build(
            (HttpStatusCode.OK, YouTubeCatalogProviderTests.SearchBody),
            (HttpStatusCode.OK, BothVideosBody));

        // Act
        await catalog.SearchAsync(Query, 1, 10, TestContext.Current.CancellationToken);
        await catalog.SearchAsync("  Never   Gonna  ", 1, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, transport.CallCount);
    }

    /// <summary>
    /// When the provider is down a stale row is served rather than nothing — the degradation rule.
    /// </summary>
    /// <remarks>
    /// `youtube-integration`: *"if YouTube is unavailable the feature degrades: queue and votes still work on
    /// cached `TrackRef` data."* An event already running must not lose the track it is playing because the
    /// provider is unreachable.
    /// </remarks>
    [Fact]
    public async Task GetAsync_WhenTheProviderIsDown_ServesTheStaleRowAsync()
    {
        // Arrange — an aged row, and a provider that fails every attempt.
        RecordingTrackRefs stored = new();
        stored.Put(
            Track() with { Title = "The old title" },
            DateTime.UtcNow - TrackRef.DefaultLifetime - TimeSpan.FromDays(1));

        (ICatalog catalog, _, _) = Build(
            stored,
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"));

        // Act
        CatalogResult<TrackMetadata> result = await catalog.GetAsync(
            VideoId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Found, result.Outcome);
        Assert.Equal("The old title", result.Value!.Title);
    }

    /// <summary>With nothing cached and the provider down, the answer is unavailable — `503`.</summary>
    [Fact]
    public async Task GetAsync_WithNothingCachedAndTheProviderDown_IsUnavailableAsync()
    {
        // Arrange
        (ICatalog catalog, _, _) = Build(
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"));

        // Act
        CatalogResult<TrackMetadata> result = await catalog.GetAsync(
            VideoId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Unavailable, result.Outcome);
    }

    /// <summary>A video the provider does not know is missing, which is `404` and not `503`.</summary>
    [Fact]
    public async Task GetAsync_WithAnUnknownVideo_IsMissingAsync()
    {
        // Arrange — the provider answers `200` with no items, which is how it says "no such video".
        (ICatalog catalog, _, _) = Build((HttpStatusCode.OK, """{"items":[]}"""));

        // Act
        CatalogResult<TrackMetadata> result = await catalog.GetAsync(
            "ZZZZZZZZZZZ",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Missing, result.Outcome);
    }

    private static TrackMetadata Track() =>
        new(VideoId, "A song", "A channel", 213, "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg");

    private static (ICatalog Catalog, CatalogTransport Transport, RecordingTrackRefs Stored) Build(
        params (HttpStatusCode Status, string Body)[] answers) =>
        Build(new RecordingTrackRefs(), answers);

    private static (ICatalog Catalog, CatalogTransport Transport, RecordingTrackRefs Stored) Build(
        RecordingTrackRefs stored,
        params (HttpStatusCode Status, string Body)[] answers)
    {
        CatalogTransport transport = new(answers);

        CatalogOptions options = new(
            YouTubeCatalogProviderTests.ApiKey,
            TrackRef.DefaultLifetime,
            TrackSearchCache.DefaultLifetime,
            CatalogOptions.DefaultMemoryLifetime,
            CatalogOptions.DefaultRequestTimeout);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ITrackRefRepository>(stored);

        services.AddInfrastructureServices(new InfrastructureOptions(
            WorkFactor,
            IsDevelopment: true,
            VerificationLinkBaseUrl: null,
            options,
            new JwtOptions(
                new string('k', JwtOptions.MinimumSigningKeyBytes),
                "musicroom-tests",
                "musicroom-tests",
                15),
            new CredentialAttemptLimits(
                CredentialAttemptLimits.DefaultPermitsPerWindow,
                TimeSpan.FromMinutes(CredentialAttemptLimits.DefaultWindowMinutes),
                CredentialAttemptLimits.DefaultMaximumPenaltyWindows)));

        services.AddHttpClient(nameof(ICatalogProvider))
            .ConfigurePrimaryHttpMessageHandler(() => transport);

        ICatalog catalog = services.BuildServiceProvider().GetRequiredService<ICatalog>();

        return (catalog, transport, stored);
    }

    /// <summary>An L2 that lives in a dictionary and counts what it was asked to write.</summary>
    private sealed class RecordingTrackRefs : ITrackRefRepository
    {
        private readonly Dictionary<string, CachedTrack> _tracks = [];
        private readonly Dictionary<string, CachedSearch> _searches = [];

        /// <summary>How many times metadata was written.</summary>
        internal int Saves { get; private set; }

        /// <summary>Puts a row in, with the age the test wants it to have.</summary>
        /// <param name="track">The metadata.</param>
        /// <param name="fetchedAtUtc">When it was last fetched.</param>
        internal void Put(TrackMetadata track, DateTime fetchedAtUtc) =>
            _tracks[track.YoutubeVideoId] = new CachedTrack(track, fetchedAtUtc);

        public Task<IReadOnlyList<CachedTrack>> FindAsync(
            IReadOnlyList<string> youtubeVideoIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<CachedTrack> found =
            [
                .. youtubeVideoIds
                    .Where(_tracks.ContainsKey)
                    .Select(id => _tracks[id]),
            ];

            return Task.FromResult(found);
        }

        public Task<CachedSearch?> FindSearchAsync(string normalisedQuery, CancellationToken cancellationToken) =>
            Task.FromResult(_searches.TryGetValue(normalisedQuery, out CachedSearch? search) ? search : null);

        public Task SaveAsync(IReadOnlyList<TrackMetadata> tracks, CancellationToken cancellationToken)
        {
            Saves++;

            foreach (TrackMetadata track in tracks)
            {
                _tracks[track.YoutubeVideoId] = new CachedTrack(track, DateTime.UtcNow);
            }

            return Task.CompletedTask;
        }

        public Task SaveSearchAsync(
            string normalisedQuery,
            IReadOnlyList<string> youtubeVideoIds,
            CancellationToken cancellationToken)
        {
            _searches[normalisedQuery] = new CachedSearch(youtubeVideoIds, DateTime.UtcNow);

            return Task.CompletedTask;
        }
    }
}
