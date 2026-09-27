using System.Net;
using System.Net.Http.Json;
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
/// The password reset pair end to end — SPEC-007, subject V.1 and V.6. A real PostgreSQL in a container,
/// because single use and the family revocation are both database predicates (ADR-0012).
/// </summary>
public sealed class PasswordResetEndpointTests : IAsyncLifetime
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string RequestsPath = "/api/v1/auth/password-reset-requests";
    internal const string ResetsPath = "/api/v1/auth/password-resets";
    internal const string NewPassword = "a brand new passphrase";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
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

    /// <summary>The flow starts: a known address gets a token — subject V.1.</summary>
    [Fact]
    public async Task Request_ForAKnownAddress_Returns202AndSendsATokenAsync()
    {
        // Arrange
        const string email = "forgot@example.com";
        await RegisterAndVerifyAsync(email);
        int before = _mail.ResetTokens.Count;

        // Act
        using HttpResponseMessage response = await PostAsync(RequestsPath, new { email });

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, _mail.ResetTokens.Count);
    }

    /// <summary>An unknown address answers identically and sends nothing — subject V.6.</summary>
    [Fact]
    public async Task Request_ForAnUnknownAddress_ReturnsTheSameAnswerAndSendsNothingAsync()
    {
        // Arrange
        const string known = "compared-reset@example.com";
        await RegisterAndVerifyAsync(known);

        using HttpResponseMessage forKnown = await PostAsync(RequestsPath, new { email = known });
        int afterKnown = _mail.ResetTokens.Count;

        // Act
        using HttpResponseMessage forUnknown = await PostAsync(
            RequestsPath,
            new { email = "nobody-at-all@example.com" });

        // Assert
        Assert.Equal(forKnown.StatusCode, forUnknown.StatusCode);
        Assert.Equal(
            await forKnown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await forUnknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(afterKnown, _mail.ResetTokens.Count);
    }

    /// <summary>Exactly one reset link is live at a time — the SPEC-004 rule, applied here.</summary>
    [Fact]
    public async Task Request_Twice_SupersedesTheFirstTokenAsync()
    {
        // Arrange
        const string email = "superseded@example.com";
        await RegisterAndVerifyAsync(email);

        string first = await RequestResetAsync(email);
        string second = await RequestResetAsync(email);
        Assert.NotEqual(first, second);

        // Act
        using HttpResponseMessage withFirst = await PostAsync(
            ResetsPath,
            new { token = first, newPassword = NewPassword });

        // Assert — the superseded link is spent, so it answers like any other spent token.
        Assert.Equal(HttpStatusCode.BadRequest, withFirst.StatusCode);

        using HttpResponseMessage withSecond = await PostAsync(
            ResetsPath,
            new { token = second, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, withSecond.StatusCode);
    }

    /// <summary>The new password signs in and the old one does not — subject V.1, end to end.</summary>
    [Fact]
    public async Task Reset_WithTheIssuedToken_Returns204AndTheNewPasswordSignsInAsync()
    {
        // Arrange
        const string email = "recovered@example.com";
        await RegisterAndVerifyAsync(email);
        string token = await RequestResetAsync(email);

        // Act
        using HttpResponseMessage reset = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using HttpResponseMessage withNew = await PostLoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        using HttpResponseMessage withOld = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    /// <summary>
    /// A reset ends every session opened with the old password — `docs/api/ENDPOINTS.md` §1 and SDD §13.1.
    /// </summary>
    [Fact]
    public async Task Reset_WithTheIssuedToken_RevokesEveryRefreshTokenFamilyAsync()
    {
        // Arrange — two sessions, so the assertion is about every family and not merely the last one.
        const string email = "sessions-ended@example.com";
        await RegisterAndVerifyAsync(email);

        TokenPair first = await SignInAsync(email, LoginEndpointTests.AccountPassword);
        TokenPair second = await SignInAsync(email, LoginEndpointTests.AccountPassword);
        string token = await RequestResetAsync(email);

        // Act
        using HttpResponseMessage reset = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Assert — neither session can rotate, and the database agrees.
        using HttpResponseMessage rotateFirst = await PostAsync(
            SessionRotationEndpointTests.TokensPath,
            new { refreshToken = first.RefreshToken });
        using HttpResponseMessage rotateSecond = await PostAsync(
            SessionRotationEndpointTests.TokensPath,
            new { refreshToken = second.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, rotateFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rotateSecond.StatusCode);
        Assert.Equal(0, await CountLiveRefreshTokensAsync(email));
    }

    /// <summary>A replayed reset link is refused, and the password it already set stands.</summary>
    [Fact]
    public async Task Reset_ReplayingASpentToken_Returns400AndLeavesThePasswordAsResetAsync()
    {
        // Arrange
        const string email = "replayed-reset@example.com";
        await RegisterAndVerifyAsync(email);
        string token = await RequestResetAsync(email);

        using HttpResponseMessage first = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Act — the same link, with a password the replayer chose.
        using HttpResponseMessage second = await PostAsync(
            ResetsPath,
            new { token, newPassword = "the attackers passphrase" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        using HttpResponseMessage signedIn = await PostLoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>Expiry is judged at the point of use, with no sweeper running — ADR-0024.</summary>
    [Fact]
    public async Task Reset_WithAnExpiredToken_Returns410Async()
    {
        // Arrange
        const string email = "expired-reset@example.com";
        await RegisterAndVerifyAsync(email);
        string token = await RequestResetAsync(email);
        await ExpireResetTokensOfAsync(email);

        // Act
        using HttpResponseMessage response = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        using HttpResponseMessage signedIn = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    /// <summary>An unknown token answers exactly like a spent one — no enumeration oracle.</summary>
    [Fact]
    public async Task Reset_WithAnUnknownToken_Returns400Async()
    {
        // Arrange — the shape the generator produces, a value it never produced.
        string token = new('a', 43);

        // Act
        using HttpResponseMessage response = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A weak new password is refused with `422`, and the link survives for another try.</summary>
    [Fact]
    public async Task Reset_WithAWeakNewPassword_Returns422AndDoesNotSpendTheTokenAsync()
    {
        // Arrange
        const string email = "weak-choice@example.com";
        await RegisterAndVerifyAsync(email);
        string token = await RequestResetAsync(email);

        // Act
        using HttpResponseMessage refused = await PostAsync(
            ResetsPath,
            new { token, newPassword = "short" });

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);

        using HttpResponseMessage retried = await PostAsync(
            ResetsPath,
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
    }

    /// <summary>
    /// The concurrency case AGENTS.md §5 requires: the requests run in parallel and the assertion is on
    /// the final database state. One `204`, one `consumed_at_utc`, and every family revoked.
    /// </summary>
    [Fact]
    public async Task Reset_TwentyParallelResetsOfOneToken_ProduceExactlyOneSuccessAsync()
    {
        // Arrange
        const string email = "contended-reset@example.com";
        const int callers = 20;
        await RegisterAndVerifyAsync(email);
        _ = await SignInAsync(email, LoginEndpointTests.AccountPassword);
        string token = await RequestResetAsync(email);

        // Act
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(_ => PostAsync(
                ResetsPath,
                new { token, newPassword = NewPassword })));

        try
        {
            // Assert
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
            Assert.Equal(
                callers - 1,
                responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
            Assert.Equal(1, await CountConsumedResetTokensAsync(email));
            Assert.Equal(0, await CountLiveRefreshTokensAsync(email));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task RegisterAndVerifyAsync(string email)
    {
        int before = _mail.Tokens.Count;

        using HttpResponseMessage registered = await PostAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(before + 1, _mail.Tokens.Count);

        using HttpResponseMessage verified = await PostAsync(
            LoginEndpointTests.VerificationsPath,
            new { token = _mail.Tokens[^1] });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    private async Task<string> RequestResetAsync(string email)
    {
        int before = _mail.ResetTokens.Count;

        using HttpResponseMessage response = await PostAsync(RequestsPath, new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, _mail.ResetTokens.Count);

        return _mail.ResetTokens[^1];
    }

    private async Task<TokenPair> SignInAsync(string email, string password)
    {
        using HttpResponseMessage response = await PostLoginAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenPair? pair = await response.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(pair);

        return pair;
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string email, string password) =>
        await PostAsync(LoginEndpointTests.SessionsPath, new { email, password });

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountLiveRefreshTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.RefreshTokens
            .AsNoTracking()
            .CountAsync(
                token => token.RevokedAtUtc == null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task<int> CountConsumedResetTokensAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.PasswordResetTokens
            .AsNoTracking()
            .CountAsync(
                token => token.ConsumedAtUtc != null
                    && context.Users.Any(user => user.Id == token.UserId && user.Email == email),
                TestContext.Current.CancellationToken);
    }

    private async Task ExpireResetTokensOfAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        // Reaching past the API on purpose: nothing in the contract can age a token, and waiting out a
        // one-hour window is not a test.
        await context.PasswordResetTokens
            .Where(token => context.Users.Any(user => user.Id == token.UserId && user.Email == email))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)),
                TestContext.Current.CancellationToken);
    }
}
