using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Catalog;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The catalog routes end to end — `docs/api/ENDPOINTS.md` §3, subject III.
/// </summary>
/// <remarks>
/// The provider is a stub registered over the real wiring: no suite may spend the project's YouTube quota
/// (T-10), and the questions here are the route's — which status, which shape, who may call it — not the
/// adapter's. `CachedCatalogTests` owns the cache behaviour and `YouTubeCatalogProviderTests` the wire format.
/// </remarks>
public sealed class TrackEndpointTests : IAsyncLifetime
{
    internal const string TracksPath = "/api/v1/tracks";
    internal const string VideoId = "dQw4w9WgXcQ";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();
    private readonly StubCatalog _catalog = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender and the catalog, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);

            // SPEC-024 made a host without a mail sender refuse to start, so every suite that boots the
            // application names one — the Development-only logging sender here, since this suite is the
            // catalog's. MailProviderStartupTests is the one that deliberately does not.
            TestMailSettings.Apply(webHost);
            webHost.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailService>(_mail);

                // The catalog is replaced, not the provider: these tests are about the route's answers, and a
                // stub at this seam keeps the quota out of CI entirely.
                services.AddScoped<ICatalog>(_ => _catalog);
            });
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>A search answers `200` with the `TrackRef` shape the contract states.</summary>
    [Fact]
    public async Task Search_WithAQuery_Returns200AndTheContractShapeAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-search@example.com");
        _catalog.Tracks = [Track()];

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}?q=never+gonna", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TrackMetadata[]? body = await response.Content.ReadFromJsonAsync<TrackMetadata[]>(
            TestContext.Current.CancellationToken);

        TrackMetadata track = Assert.Single(body!);
        Assert.Equal(VideoId, track.YoutubeVideoId);
        Assert.Equal("A song", track.Title);
        Assert.Equal("A channel", track.Channel);
        Assert.Equal(213, track.DurationSeconds);
        Assert.False(string.IsNullOrWhiteSpace(track.ThumbnailUrl));
    }

    /// <summary>The query reaches the handler, and the paging arguments with it.</summary>
    [Fact]
    public async Task Search_PassesTheQueryAndThePagingThroughAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-paging@example.com");
        _catalog.Tracks = [];

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}?q=never+gonna&page=2&pageSize=5", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("never gonna", _catalog.LastQuery);
        Assert.Equal(2, _catalog.LastPage);
        Assert.Equal(5, _catalog.LastPageSize);
    }

    /// <summary>An empty query is `422`, not a hundred quota units spent on nothing.</summary>
    [Fact]
    public async Task Search_WithNoQuery_Returns422Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-blank@example.com");

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri(TracksPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Null(_catalog.LastQuery);
        Assert.Equal(["Query"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>A page size above the cap is `422` — an unbounded list is OWASP API4.</summary>
    [Fact]
    public async Task Search_WithTooLargeAPageSize_Returns422Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-page-size@example.com");

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}?q=never+gonna&pageSize=500", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["PageSize"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>One video resolves to `200`.</summary>
    [Fact]
    public async Task Get_WithAKnownVideo_Returns200Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-get@example.com");
        _catalog.Single = Track();

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}/{VideoId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TrackMetadata? body = await response.Content.ReadFromJsonAsync<TrackMetadata>(
            TestContext.Current.CancellationToken);
        Assert.Equal(VideoId, body!.YoutubeVideoId);
    }

    /// <summary>A video the catalog does not know is `404`, which is not the same as `503`.</summary>
    [Fact]
    public async Task Get_WithAnUnknownVideo_Returns404Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-unknown@example.com");
        _catalog.Outcome = CatalogOutcome.Missing;

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}/ZZZZZZZZZZZ", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A spent quota or an unreachable provider is `503` with a problem document, never `500`.
    /// </summary>
    [Fact]
    public async Task Get_WhenTheCatalogIsUnavailable_Returns503Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-unavailable@example.com");
        _catalog.Outcome = CatalogOutcome.Unavailable;

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}/{VideoId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — and the body says which side is at fault without naming the provider.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same for a search, because the quota is shared by both routes.</summary>
    [Fact]
    public async Task Search_WhenTheCatalogIsUnavailable_Returns503Async()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("catalog-search-down@example.com");
        _catalog.Outcome = CatalogOutcome.Unavailable;

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri($"{TracksPath}?q=never+gonna", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// Neither route is anonymous: the key is ours, and so is the quota behind it.
    /// </summary>
    [Theory]
    [InlineData(TracksPath + "?q=never+gonna")]
    [InlineData(TracksPath + "/" + VideoId)]
    public async Task Routes_WithoutAToken_Return401Async(string path)
    {
        // Arrange
        using HttpClient anonymous = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(path, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static TrackMetadata Track() =>
        new(VideoId, "A song", "A channel", 213, "https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg");

    private async Task<HttpClient> SignInAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        return client;
    }

    /// <summary>A catalog that answers from fields a test sets, and records what it was asked.</summary>
    private sealed class StubCatalog : ICatalog
    {
        /// <summary>What a search returns.</summary>
        internal IReadOnlyList<TrackMetadata> Tracks { get; set; } = [];

        /// <summary>
        /// What a single read returns.
        /// </summary>
        /// <remarks>
        /// Named <c>Single</c> rather than <c>Track</c> so it does not shadow the outer suite's helper of that
        /// name, which the analyzer refuses (S3218).
        /// </remarks>
        internal TrackMetadata? Single { get; set; }

        /// <summary>The outcome to answer with.</summary>
        internal CatalogOutcome Outcome { get; set; } = CatalogOutcome.Found;

        /// <summary>The query the route passed down, or null when it never reached here.</summary>
        internal string? LastQuery { get; private set; }

        /// <summary>The page the route passed down.</summary>
        internal int LastPage { get; private set; }

        /// <summary>The page size the route passed down.</summary>
        internal int LastPageSize { get; private set; }

        public Task<CatalogResult<IReadOnlyList<TrackMetadata>>> SearchAsync(
            string query,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastPage = page;
            LastPageSize = pageSize;

            return Task.FromResult(Outcome == CatalogOutcome.Found
                ? CatalogResult<IReadOnlyList<TrackMetadata>>.Found(Tracks)
                : new CatalogResult<IReadOnlyList<TrackMetadata>>(Outcome, null));
        }

        public Task<CatalogResult<TrackMetadata>> GetAsync(
            string youtubeVideoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Outcome == CatalogOutcome.Found && Single is not null
                ? CatalogResult<TrackMetadata>.Found(Single)
                : new CatalogResult<TrackMetadata>(Outcome, null));
    }
}
