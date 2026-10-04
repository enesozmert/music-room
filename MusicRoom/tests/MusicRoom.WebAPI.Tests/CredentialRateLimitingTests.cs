using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The credential rate limits end to end — SPEC-006, subject V.6, threat T-25.
/// </summary>
/// <remarks>
/// Each test builds its own host with the limits it is about, so the limiter state is fresh and the
/// order the suite runs in cannot change a result. One PostgreSQL container is shared, because the
/// limits are in process and have nothing to do with the database.
/// </remarks>
public sealed class CredentialRateLimitingTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string ClientIpHeader = "X-Test-Client-Ip";
    internal const string Password = "correct horse battery";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    /// <summary>Starts the engine and brings the schema up once.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        await using WebApplicationFactory<Program> factory = CreateFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    /// <summary>The per-IP limit fires, and the refusal carries the header the standard requires.</summary>
    [Fact]
    public async Task Login_BeyondThePerIpLimit_Returns429WithRetryAfterAsync()
    {
        // Arrange — three per window from one address, and an account limit high enough to stay out of it.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_SESSIONS_PERMITS", "3"),
            ("RATE_LIMIT_ACCOUNT_PERMITS", TestRateLimitSettings.Permits));

        // Act — every request names a different address, so only the IP partition can refuse them.
        HttpStatusCode[] answers = new HttpStatusCode[4];

        for (int attempt = 0; attempt < answers.Length; attempt++)
        {
            using HttpResponseMessage response = await PostLoginAsync(
                factory,
                $"ip-limited-{attempt}@example.com",
                Password,
                clientIp: "203.0.113.10");

            answers[attempt] = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Assert — Retry-After, in seconds, on the refusal itself.
                Assert.NotNull(response.Headers.RetryAfter);
                Assert.True(response.Headers.RetryAfter.Delta > TimeSpan.Zero);
                Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        // Assert
        Assert.Equal(3, answers.Count(status => status == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }

    /// <summary>A second client is not refused by another client's counter.</summary>
    [Fact]
    public async Task Login_FromASecondIp_IsNotRefusedByAnotherClientsCounterAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_SESSIONS_PERMITS", "2"),
            ("RATE_LIMIT_ACCOUNT_PERMITS", TestRateLimitSettings.Permits));

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage _ = await PostLoginAsync(
                factory,
                $"first-client-{attempt}@example.com",
                Password,
                clientIp: "203.0.113.20");
        }

        // Act
        using HttpResponseMessage second = await PostLoginAsync(
            factory,
            "second-client@example.com",
            Password,
            clientIp: "203.0.113.21");

        // Assert — refused for the wrong password, not for the other client's traffic.
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    /// <summary>
    /// The per-account limit fires on repeated failures for one address, and leaves every other address
    /// alone — "account **and** IP", `authentication-authorization`.
    /// </summary>
    [Fact]
    public async Task Login_BeyondThePerAccountLimit_Returns429ForThatAddressOnlyAsync()
    {
        // Arrange — two failures per account, and an IP allowance nothing here reaches.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_ACCOUNT_PERMITS", "2"),
            ("RATE_LIMIT_SESSIONS_PERMITS", TestRateLimitSettings.Permits));

        const string targeted = "guessed@example.com";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage refused = await PostLoginAsync(factory, targeted, "wrong password");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // Act
        using HttpResponseMessage throttled = await PostLoginAsync(factory, targeted, "wrong password");
        using HttpResponseMessage other = await PostLoginAsync(factory, "untouched@example.com", Password);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    /// <summary>T-25's row-creation loop is bounded.</summary>
    [Fact]
    public async Task Registrations_BeyondTheLimit_Returns429Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_REGISTRATIONS_PERMITS", "2"),
            ("RATE_LIMIT_ACCOUNT_PERMITS", TestRateLimitSettings.Permits));

        // Act
        HttpStatusCode[] answers = new HttpStatusCode[3];

        for (int attempt = 0; attempt < answers.Length; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(
                factory,
                LoginEndpointTests.RegistrationPath,
                new
                {
                    email = $"looped-{attempt}@example.com",
                    password = Password,
                    displayName = "Player One",
                });

            answers[attempt] = response.StatusCode;
        }

        // Assert
        Assert.Equal(2, answers.Count(status => status == HttpStatusCode.Accepted));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }

    /// <summary>
    /// T-19: the mail loop is bounded **before** issue O17 puts a real sender behind this endpoint.
    /// </summary>
    [Fact]
    public async Task VerificationRequests_BeyondTheLimit_Returns429AndSendsNoFurtherMailAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_VERIFICATION_REQUESTS_PERMITS", "2"),
            ("RATE_LIMIT_ACCOUNT_PERMITS", TestRateLimitSettings.Permits));

        // Act
        HttpStatusCode[] answers = new HttpStatusCode[3];

        for (int attempt = 0; attempt < answers.Length; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(
                factory,
                "/api/v1/auth/verification-requests",
                new { email = $"resend-loop-{attempt}@example.com" });

            answers[attempt] = response.StatusCode;
        }

        // Assert — and no mail was sent at all, because none of these addresses is registered.
        Assert.Equal(2, answers.Count(status => status == HttpStatusCode.Accepted));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
        Assert.Empty(_mail.Tokens);
    }

    /// <summary>The verification-token guessing loop is bounded.</summary>
    [Fact]
    public async Task EmailVerifications_BeyondTheLimit_Returns429Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_EMAIL_VERIFICATIONS_PERMITS", "2"));

        // Act
        HttpStatusCode[] answers = new HttpStatusCode[3];

        for (int attempt = 0; attempt < answers.Length; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(
                factory,
                LoginEndpointTests.VerificationsPath,
                new { token = new string('a', 43) });

            answers[attempt] = response.StatusCode;
        }

        // Assert
        Assert.Equal(2, answers.Count(status => status == HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }

    /// <summary>The refresh-token guessing loop is bounded.</summary>
    [Fact]
    public async Task Tokens_BeyondTheLimit_Returns429Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_TOKENS_PERMITS", "2"));

        // Act
        HttpStatusCode[] answers = new HttpStatusCode[3];

        for (int attempt = 0; attempt < answers.Length; attempt++)
        {
            using HttpResponseMessage response = await PostAsync(
                factory,
                SessionRotationEndpointTests.TokensPath,
                new { refreshToken = new string('b', 43) });

            answers[attempt] = response.StatusCode;
        }

        // Assert
        Assert.Equal(2, answers.Count(status => status == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }

    /// <summary>
    /// A throttled attempt leaves a `throttled` row, and the response is unchanged — SPEC-009 escalation 2.
    /// Before it, the refusal threw and SPEC-008's fail-closed behaviour rolled the row back with it.
    /// </summary>
    [Fact]
    public async Task Login_BeyondThePerAccountLimit_WritesAThrottledAuditRecordAnd429Async()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_ACCOUNT_PERMITS", "2"),
            ("RATE_LIMIT_SESSIONS_PERMITS", TestRateLimitSettings.Permits));

        const string targeted = "throttle-audited@example.com";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage refused = await PostLoginAsync(factory, targeted, "wrong password");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // Act
        using HttpResponseMessage throttled = await PostLoginAsync(factory, targeted, "wrong password");

        // Assert — the response is byte-for-byte what SPEC-006 shipped.
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
        Assert.Empty(await throttled.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // And the row exists, which it did not before this spec.
        using IServiceScope scope = factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        List<string> outcomes = await context.AuditRecords
            .AsNoTracking()
            .Where(record => record.Action == "auth.login")
            .Select(record => record.Outcome)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("throttled", outcomes, StringComparer.Ordinal);
        Assert.Equal(2, outcomes.Count(outcome => outcome == "refused"));
    }

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);

            foreach ((string key, string value) in settings)
            {
                webHost.UseSetting(key, value);
            }

            webHost.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailService>(_mail);

                // The test client has no remote address, so every request would otherwise share the one
                // "unknown" partition and the per-IP tests could not tell two clients apart. Production
                // reads Connection.RemoteIpAddress and trusts no header — this filter is test-only, and
                // it runs before UseRateLimiter because a startup filter wraps the application's own
                // pipeline.
                services.AddSingleton<IStartupFilter>(new ClientIpFromHeaderStartupFilter());
            });
        });

    private static async Task<HttpResponseMessage> PostLoginAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string password,
        string? clientIp = null) =>
        await PostAsync(factory, LoginEndpointTests.SessionsPath, new { email, password }, clientIp);

    private static async Task<HttpResponseMessage> PostAsync(
        WebApplicationFactory<Program> factory,
        string path,
        object body,
        string? clientIp = null)
    {
        using HttpClient client = factory.CreateAuditedClient();

        if (clientIp is not null)
        {
            client.DefaultRequestHeaders.Add(ClientIpHeader, clientIp);
        }

        return await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Sets the connection's remote address from a test header, before the application's own pipeline.
    /// </summary>
    private sealed class ClientIpFromHeaderStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            builder =>
            {
                builder.Use(async (context, following) =>
                {
                    if (context.Request.Headers.TryGetValue(ClientIpHeader, out var value)
                        && System.Net.IPAddress.TryParse(value.ToString(), out System.Net.IPAddress? address))
                    {
                        // Through ConnectionInfo, which creates the connection feature on demand.
                        // TestServer does not populate IHttpConnectionFeature itself.
                        context.Connection.RemoteIpAddress = address;
                    }

                    await following();
                });

                next(builder);
            };
    }
}
