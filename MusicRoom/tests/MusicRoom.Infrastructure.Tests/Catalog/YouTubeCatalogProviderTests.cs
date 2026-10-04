using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Domain.Entities;
using MusicRoom.Infrastructure;
using MusicRoom.Infrastructure.Catalog;
using MusicRoom.Infrastructure.Messaging;
using MusicRoom.Infrastructure.Security;
using MusicRoom.Infrastructure.Tests.Messaging;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Catalog;

/// <summary>
/// The provider adapter: what it asks YouTube for, what it makes of the answer, and what it does when there is
/// no answer — ADR-0003, `youtube-integration`.
/// </summary>
public sealed class YouTubeCatalogProviderTests
{
    internal const string ApiKey = "a-key-that-stands-in-for-the-real-one";
    internal const string VideoId = "dQw4w9WgXcQ";

    // One attempt plus the retries the pipeline is configured for. Written out rather than read from the
    // registration's own constant: a test that asserts against the value it is testing cannot notice it change.
    internal const int ExpectedAttempts = 3;

    // What one `search.list` costs against the daily quota, per the provider's published table.
    internal const string SearchQuotaUnits = "100";

    // The BCrypt cost the unrelated half of the registration needs; nothing here hashes anything.
    internal const int WorkFactor = 12;

    internal const string SearchBody = """
        {"items":[{"id":{"videoId":"dQw4w9WgXcQ"}},{"id":{"videoId":"oHg5SJYRHA0"}},{"id":{}}]}
        """;

    internal const string VideoBody = """
        {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"A song","channelTitle":"A channel",
        "thumbnails":{"medium":{"url":"https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg"}}},
        "contentDetails":{"duration":"PT3M33S"}}]}
        """;

    /// <summary>The search asks for ids only, with the key, and keeps the provider's ranking.</summary>
    [Fact]
    public async Task SearchAsync_AsksForIdsAndKeepsTheOrderAsync()
    {
        // Arrange
        (ICatalogProvider provider, CatalogTransport transport, _) = Build(
            (HttpStatusCode.OK, SearchBody));

        // Act
        CatalogResult<IReadOnlyList<string>> result = await provider.SearchAsync(
            "never gonna",
            10,
            TestContext.Current.CancellationToken);

        // Assert — part=id keeps the response small, type=video keeps channels out of a track list.
        string request = Assert.Single(transport.Requests);
        Assert.Contains("youtube/v3/search", request, StringComparison.Ordinal);
        Assert.Contains("part=id", request, StringComparison.Ordinal);
        Assert.Contains("type=video", request, StringComparison.Ordinal);
        Assert.Contains(ApiKey, request, StringComparison.Ordinal);

        Assert.Equal(CatalogOutcome.Found, result.Outcome);
        Assert.Equal(["dQw4w9WgXcQ", "oHg5SJYRHA0"], result.Value);
    }

    /// <summary>Several ids are one call, which is why `videos.list` is the preferred read.</summary>
    [Fact]
    public async Task GetAsync_ResolvesEveryIdInOneCallAsync()
    {
        // Arrange
        (ICatalogProvider provider, CatalogTransport transport, _) = Build((HttpStatusCode.OK, VideoBody));

        // Act
        CatalogResult<IReadOnlyList<TrackMetadata>> result = await provider.GetAsync(
            [VideoId, "oHg5SJYRHA0"],
            TestContext.Current.CancellationToken);

        // Assert
        string request = Assert.Single(transport.Requests);
        Assert.Contains("youtube/v3/videos", request, StringComparison.Ordinal);
        Assert.Contains("dQw4w9WgXcQ%2CoHg5SJYRHA0", request, StringComparison.Ordinal);

        TrackMetadata track = Assert.Single(result.Value!);
        Assert.Equal(VideoId, track.YoutubeVideoId);
        Assert.Equal("A song", track.Title);
        Assert.Equal("A channel", track.Channel);
        Assert.Equal(213, track.DurationSeconds);
        Assert.Contains("mqdefault.jpg", track.ThumbnailUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live stream has no duration, and is still a track — zero rather than a refusal.
    /// </summary>
    [Fact]
    public async Task GetAsync_WithNoDuration_ReportsZeroAsync()
    {
        // Arrange
        const string live = """
            {"items":[{"id":"dQw4w9WgXcQ","snippet":{"title":"Live","channelTitle":"A channel",
            "thumbnails":{"default":{"url":"https://i.ytimg.com/vi/x/default.jpg"}}},"contentDetails":{}}]}
            """;
        (ICatalogProvider provider, _, _) = Build((HttpStatusCode.OK, live));

        // Act
        CatalogResult<IReadOnlyList<TrackMetadata>> result = await provider.GetAsync(
            [VideoId],
            TestContext.Current.CancellationToken);

        // Assert — and the default thumbnail is used when there is no medium one.
        TrackMetadata track = Assert.Single(result.Value!);
        Assert.Equal(0, track.DurationSeconds);
        Assert.Contains("default.jpg", track.ThumbnailUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// `403` is the quota, and it is an outcome rather than an exception — the route answers `503`.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WhenTheQuotaIsSpent_IsUnavailableAsync()
    {
        // Arrange — the provider's own error document, which must not reach a caller or a log.
        const string refusal = """
            {"error":{"code":403,"errors":[{"reason":"quotaExceeded","message":"quota exceeded"}]}}
            """;
        (ICatalogProvider provider, _, LoggingMailServiceTests.MessageLog log) = Build(
            (HttpStatusCode.Forbidden, refusal));

        // Act
        CatalogResult<IReadOnlyList<string>> result = await provider.SearchAsync(
            "never gonna",
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Value);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("quota exceeded", StringComparison.Ordinal));
    }

    /// <summary>A provider `5xx` is the same outcome, and the retries are bounded.</summary>
    [Fact]
    public async Task GetAsync_WhenTheProviderFails_IsUnavailableAfterBoundedRetriesAsync()
    {
        // Arrange — more failures than the pipeline will attempt.
        (ICatalogProvider provider, CatalogTransport transport, _) = Build(
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"),
            (HttpStatusCode.BadGateway, "{}"));

        // Act
        CatalogResult<IReadOnlyList<TrackMetadata>> result = await provider.GetAsync(
            [VideoId],
            TestContext.Current.CancellationToken);

        // Assert — one attempt plus the configured retries, and no more.
        Assert.Equal(CatalogOutcome.Unavailable, result.Outcome);
        Assert.Equal(ExpectedAttempts, transport.CallCount);
    }

    /// <summary>
    /// The quota cost is in the log and the key is not — `youtube-integration`, `secrets-management`.
    /// </summary>
    [Fact]
    public async Task SearchAsync_LogsTheQuotaCostAndNotTheKeyAsync()
    {
        // Arrange
        (ICatalogProvider provider, _, LoggingMailServiceTests.MessageLog log) = Build(
            (HttpStatusCode.OK, SearchBody));

        // Act
        await provider.SearchAsync("never gonna", 10, TestContext.Current.CancellationToken);

        // Assert
        string message = Assert.Single(log.Messages);
        Assert.Contains("search.list", message, StringComparison.Ordinal);
        Assert.Contains(SearchQuotaUnits, message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no key configured nothing is called at all, and the catalog reports itself unavailable.
    /// </summary>
    /// <remarks>
    /// A host with no key still runs: the caches answer, and `youtube-integration`'s degradation rule holds.
    /// </remarks>
    [Fact]
    public async Task SearchAsync_WithNoKey_CallsNothingAsync()
    {
        // Arrange
        (ICatalogProvider provider, CatalogTransport transport, _) = Build(apiKey: null);

        // Act
        CatalogResult<IReadOnlyList<string>> result = await provider.SearchAsync(
            "never gonna",
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CatalogOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, transport.CallCount);
    }

    private static (ICatalogProvider Provider, CatalogTransport Transport, LoggingMailServiceTests.MessageLog Log)
        Build(params (HttpStatusCode Status, string Body)[] answers) =>
        Build(ApiKey, answers);

    private static (ICatalogProvider Provider, CatalogTransport Transport, LoggingMailServiceTests.MessageLog Log)
        Build(string? apiKey, params (HttpStatusCode Status, string Body)[] answers)
    {
        CatalogTransport transport = new(answers);
        LoggingMailServiceTests.MessageLog log = new();

        CatalogOptions options = new(
            apiKey,
            TrackRef.DefaultLifetime,
            TrackSearchCache.DefaultLifetime,
            CatalogOptions.DefaultMemoryLifetime,
            CatalogOptions.DefaultRequestTimeout);

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ILoggerProvider>(new CatalogLoggerProvider(log));

        // L2 is not involved here: this suite is the adapter, and the cache has its own.
        services.AddSingleton<MusicRoom.Application.Abstractions.Persistence.ITrackRefRepository>(
            new NullTrackRefRepository());

        // Through the public composition-root entry point, which is the registration the application runs. The
        // other adapters it sets up are irrelevant here and cost nothing to have.
        services.AddInfrastructureServices(new InfrastructureOptions(
            WorkFactor,
            IsDevelopment: true,
            new MailOptions(MailOptions.LoggingProvider, null, null, null, null, null),
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

        ICatalogProvider provider = services.BuildServiceProvider().GetRequiredService<ICatalogProvider>();

        return (provider, transport, log);
    }

    /// <summary>Feeds the provider's own category into one log, and drops the rest.</summary>
    private sealed class CatalogLoggerProvider : ILoggerProvider
    {
        // Internal rather than private: the naming rule asks a private field for an underscore prefix, and a
        // const is a field to that rule.
        internal const string Category = "MusicRoom.Infrastructure.Catalog.YouTubeCatalogProvider";

        private readonly LoggingMailServiceTests.MessageLog _log;

        internal CatalogLoggerProvider(LoggingMailServiceTests.MessageLog log) => _log = log;

        public ILogger CreateLogger(string categoryName) =>
            categoryName == Category
                ? new LoggingMailServiceTests.RecordingLogger<YouTubeCatalogProviderTests>(_log)
                : NullLogger.Instance;

        public void Dispose()
        {
            // Nothing to release: the log outlives this provider, because the test reads it afterwards.
        }
    }

    /// <summary>An L2 that holds nothing, for the suite that is not about L2.</summary>
    private sealed class NullTrackRefRepository : MusicRoom.Application.Abstractions.Persistence.ITrackRefRepository
    {
        public Task<IReadOnlyList<MusicRoom.Application.Abstractions.Persistence.CachedTrack>> FindAsync(
            IReadOnlyList<string> youtubeVideoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MusicRoom.Application.Abstractions.Persistence.CachedTrack>>([]);

        public Task<MusicRoom.Application.Abstractions.Persistence.CachedSearch?> FindSearchAsync(
            string normalisedQuery,
            CancellationToken cancellationToken) =>
            Task.FromResult<MusicRoom.Application.Abstractions.Persistence.CachedSearch?>(null);

        public Task SaveAsync(IReadOnlyList<TrackMetadata> tracks, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveSearchAsync(
            string normalisedQuery,
            IReadOnlyList<string> youtubeVideoIds,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
