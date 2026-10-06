using System.Net;
using System.Net.Http.Json;
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
/// Changing a password while signed in — SPEC-028, ASVS V6.2.2, V6.2.3, V7.4.3 and V7.5.1, threat T-31.
/// </summary>
/// <remarks>
/// The test that carries the decision is `Change_RevokesTheOtherSessionsAndKeepsThisOne`: ASVS asks that the
/// *other* sessions end, and logging somebody out of the device they just used to change their password buys
/// nothing.
/// </remarks>
public sealed class PasswordChangeEndpointTests : IAsyncLifetime
{
    internal const string Path = "/api/v1/me/password";
    internal const string NewPassword = "a-replacement-password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = CreateFactory();

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

    /// <summary>The happy path: `204`, the new password works, the old one does not.</summary>
    [Fact]
    public async Task Change_WithTheCurrentPassword_Returns204AndReplacesItAsync()
    {
        // Arrange
        const string email = "password-change@example.com";
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);
        using HttpClient caller = client;

        // Act
        using HttpResponseMessage response = await ChangeAsync(caller, LoginEndpointTests.AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(email, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(email, LoginEndpointTests.AccountPassword));
    }

    /// <summary>
    /// Every other session ends; the one that made the call does not — ASVS V7.4.3, SPEC-028 decision 4.
    /// </summary>
    [Fact]
    public async Task Change_RevokesTheOtherSessionsAndKeepsThisOneAsync()
    {
        // Arrange — two sessions for one account: the caller's, and one opened on another device.
        const string email = "password-change-sessions@example.com";
        (HttpClient client, TokenPair caller) = await TestBearer.SignInAsync(_factory, _mail, email);
        using HttpClient signedIn = client;
        TokenPair other = await TestBearer.SignInAgainAsync(_factory, email);

        // Act
        using HttpResponseMessage response = await ChangeAsync(signedIn, LoginEndpointTests.AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await RotateStatusAsync(other.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, await RotateStatusAsync(caller.RefreshToken));
    }

    /// <summary>
    /// A wrong current password is `422` and changes nothing — and it is not `401`, because the token is fine.
    /// </summary>
    [Fact]
    public async Task Change_WithTheWrongCurrentPassword_Returns422AndChangesNothingAsync()
    {
        // Arrange
        const string email = "password-change-wrong@example.com";
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);
        using HttpClient caller = client;

        // Act
        using HttpResponseMessage response = await ChangeAsync(caller, "not the current password");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(email, LoginEndpointTests.AccountPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(email, NewPassword));
    }

    /// <summary>A new password that breaks the policy is `422`, by registration's own rules.</summary>
    [Theory]
    [InlineData("short")]
    [InlineData(LoginEndpointTests.AccountPassword)]
    public async Task Change_WithAnUnacceptableNewPassword_Returns422Async(string newPassword)
    {
        ArgumentNullException.ThrowIfNull(newPassword);

        // Arrange — the second case is the current password again, which would revoke sessions for a no-op.
        (HttpClient client, _) = await TestBearer.SignInAsync(
            _factory, _mail, $"password-change-policy-{newPassword.Length}@example.com");
        using HttpClient caller = client;

        // Act
        using HttpResponseMessage response = await caller.PostAsJsonAsync(
            Path,
            new { currentPassword = LoginEndpointTests.AccountPassword, newPassword },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The body's key is part of the contract: a client reads `errors.NewPassword`. SPEC-034 moved the policy
        // into one shared rule, and this holds that rule to this command's own property name.
        Assert.Equal(["NewPassword"], await ValidationKeysAsync(response));
    }

    /// <summary>Both outcomes leave an audit row, and the action names the change — subject V.6.</summary>
    [Fact]
    public async Task Change_WritesAnAuditRecordForBothOutcomesAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(
            _factory, _mail, "password-change-audit@example.com");
        using HttpClient caller = client;

        // Act
        using HttpResponseMessage refused = await ChangeAsync(caller, "not the current password");
        using HttpResponseMessage changed = await ChangeAsync(caller, LoginEndpointTests.AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(1, await AuditCountAsync("refused"));
        Assert.Equal(1, await AuditCountAsync("completed"));
    }

    /// <summary>
    /// Repeated wrong attempts are refused with `429` and `Retry-After` — the per-account limiter.
    /// </summary>
    /// <remarks>
    /// Without this the route would be an oracle for guessing the password at request speed, given nothing more
    /// than a stolen access token.
    /// </remarks>
    [Fact]
    public async Task Change_WithRepeatedWrongPasswords_IsThrottledAsync()
    {
        // Arrange — three attempts per window, and a per-IP policy high enough to stay out of the way.
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_ACCOUNT_PERMITS", "3"),
            ("RATE_LIMIT_PASSWORD_CHANGES_PERMITS", TestRateLimitSettings.Permits));

        (HttpClient client, _) = await TestBearer.SignInAsync(
            factory, _mail, "password-change-throttled@example.com");
        using HttpClient caller = client;

        // Act — four wrong attempts: the fourth is out of allowance.
        HttpStatusCode last = HttpStatusCode.Unused;
        string? retryAfter = null;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            using HttpResponseMessage response = await ChangeAsync(caller, "not the current password");
            last = response.StatusCode;
            retryAfter = response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values)
                ? values.First()
                : null;
        }

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.False(string.IsNullOrWhiteSpace(retryAfter));
    }

    /// <summary>A correct password clears the history, so a later typo is not already at the limit.</summary>
    [Fact]
    public async Task Change_AfterASuccess_StartsItsAllowanceAgainAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory(
            ("RATE_LIMIT_ACCOUNT_PERMITS", "3"),
            ("RATE_LIMIT_PASSWORD_CHANGES_PERMITS", TestRateLimitSettings.Permits));

        (HttpClient client, _) = await TestBearer.SignInAsync(
            factory, _mail, "password-change-reset@example.com");
        using HttpClient caller = client;

        using HttpResponseMessage firstWrong = await ChangeAsync(caller, "not the current password");
        using HttpResponseMessage secondWrong = await ChangeAsync(caller, "still not it");
        using HttpResponseMessage correct = await ChangeAsync(caller, LoginEndpointTests.AccountPassword);

        // Act — two more wrong attempts, which would be the fourth and fifth without the reset.
        using HttpResponseMessage afterOne = await ChangeAsync(caller, "wrong again");
        using HttpResponseMessage afterTwo = await ChangeAsync(caller, "wrong once more");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, firstWrong.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondWrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, afterOne.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, afterTwo.StatusCode);
    }

    /// <summary>The route is not a way to change a password without a token.</summary>
    [Fact]
    public async Task Change_WithoutAToken_Returns401Async()
    {
        // Arrange
        using HttpClient anonymous = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            Path,
            new { currentPassword = LoginEndpointTests.AccountPassword, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    /// <summary>
    /// The property name a `422` keys its `errors` dictionary on.
    /// </summary>
    /// <remarks>
    /// Added by SPEC-034, and among the first assertions of their kind in this repository: 37 assertions check
    /// `HttpStatusCode.UnprocessableEntity` and none checked the body, so a refactor that renamed a validated
    /// property would have changed a published response shape with nothing failing. This path's key is
    /// `NewPassword`, which is the property this command exposes — not the shared rule's idea of a name.
    /// </remarks>
    private static async Task<string[]> ValidationKeysAsync(HttpResponseMessage response)
    {
        using System.IO.Stream body = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);

        using System.Text.Json.JsonDocument document = await System.Text.Json.JsonDocument.ParseAsync(
            body,
            cancellationToken: TestContext.Current.CancellationToken);

        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }
    private static Task<HttpResponseMessage> ChangeAsync(HttpClient caller, string currentPassword) =>
        caller.PostAsJsonAsync(
            Path,
            new { currentPassword, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

    private async Task<HttpStatusCode> SignInStatusAsync(string email, string password)
    {
        using HttpClient anonymous = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            LoginEndpointTests.SessionsPath,
            new { email, password },
            TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private async Task<HttpStatusCode> RotateStatusAsync(string refreshToken)
    {
        using HttpClient anonymous = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/tokens",
            new { refreshToken },
            TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private async Task<int> AuditCountAsync(string outcome)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .CountAsync(
                record => record.Action == "account.password-change" && record.Outcome == outcome,
                TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);

            foreach ((string key, string value) in settings)
            {
                webHost.UseSetting(key, value);
            }

            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
        });
}
