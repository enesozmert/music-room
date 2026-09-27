using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The audit log, written by a real request against a real engine — subject V.6, SDD §13.4, SPEC-008.
/// </summary>
public sealed class AuditRecordEndpointTests : IAsyncLifetime
{
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

    /// <summary>One action, one row — the requirement subject V.6 states.</summary>
    [Fact]
    public async Task Mutation_WithHeaders_WritesExactlyOneAuditRecordAsync()
    {
        // Arrange
        const string email = "audited@example.com";

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AuditRecord record = Assert.Single(await RecordsForAsync("auth.register"));
        Assert.Equal("account", record.TargetType);
        Assert.NotEqual(default, record.OccurredAtUtc);
    }

    /// <summary>The three fields subject V.6 names come from the client's own headers.</summary>
    [Fact]
    public async Task AuditRecord_CarriesThePlatformDeviceAndAppVersionAsync()
    {
        // Arrange and act
        using HttpResponseMessage response = await PostVerificationRequestAsync("fields@example.com");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Assert
        AuditRecord record = Assert.Single(await RecordsForAsync("auth.request-verification"));
        Assert.Equal(TestClientContext.Platform, record.Platform);
        Assert.Equal(TestClientContext.Device, record.Device);
        Assert.Equal(TestClientContext.AppVersion, record.AppVersion);
        Assert.False(string.IsNullOrWhiteSpace(record.CorrelationId));
    }

    /// <summary>
    /// An anonymous mutation is still recorded, with no <c>userId</c> and a named action — SPEC-008
    /// escalation 3, option A, made explicit rather than surprising at defence.
    /// </summary>
    [Fact]
    public async Task AuditRecord_ForAnAnonymousMutation_HasNoUserIdAndStillNamesTheActionAsync()
    {
        // Arrange and act — a login that fails is still an action, and nobody is authenticated for it.
        using HttpResponseMessage response = await PostLoginAsync("nobody-here@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Assert
        AuditRecord record = Assert.Single(await RecordsForAsync("auth.login"));
        Assert.Null(record.UserId);
        Assert.Equal("session", record.TargetType);
    }

    /// <summary>A command refused by validation never happened, so it leaves no record.</summary>
    [Fact]
    public async Task AuditRecord_ForARejectedCommand_IsNotWrittenAsync()
    {
        // Arrange and act — a password below the policy minimum is a 422 from the validation behaviour.
        using HttpClient client = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            LoginEndpointTests.RegistrationPath,
            new { email = "rejected@example.com", password = "short", displayName = "Player One" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await RecordsForEmailAsync("rejected@example.com"));
    }

    /// <summary>
    /// §13.5 applies to this table too: no column may hold an address, a token or a password. The check is
    /// on every row the suite produced, not on one.
    /// </summary>
    [Fact]
    public async Task AuditRecord_CarriesNoAddressTokenOrPasswordAsync()
    {
        // Arrange
        const string email = "leak-check@example.com";
        using HttpResponseMessage registered = await PostRegistrationAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        // Act
        IReadOnlyList<AuditRecord> records = await AllRecordsAsync();

        // Assert
        Assert.NotEmpty(records);

        foreach (AuditRecord record in records)
        {
            string row = string.Join(
                '|',
                record.Action,
                record.TargetType,
                record.Platform,
                record.Device,
                record.AppVersion,
                record.CorrelationId,
                record.Outcome);

            Assert.DoesNotContain("@", row, StringComparison.Ordinal);
            Assert.DoesNotContain(LoginEndpointTests.AccountPassword, row, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The record and the effect commit together: the account exists and its record exists, from one request
    /// — SPEC-008 escalations 1 and 2.
    /// </summary>
    [Fact]
    public async Task AuditRecord_AndTheEffectItDescribes_BothExistAfterOneRequestAsync()
    {
        // Arrange
        const string email = "atomic@example.com";

        // Act
        using HttpResponseMessage response = await PostRegistrationAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Assert.True(await context.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == email, TestContext.Current.CancellationToken));
        Assert.Single(await RecordsForAsync("auth.register"));
    }

    /// <summary>A wrong password is recorded as refused, and names no account — SPEC-009, A09.</summary>
    [Fact]
    public async Task Login_WithWrongPassword_WritesARefusedAuditRecordAsync()
    {
        // Arrange
        const string email = "refused-login@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync(email, "not the password at all");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        AuditRecord record = Assert.Single(await RecordsForAsync("auth.login"));
        Assert.Equal("refused", record.Outcome);

        // Deliberately unnamed: a user_id on a failed login would make the table an account-existence oracle.
        Assert.Null(record.UserId);
    }

    /// <summary>A successful sign-in names the account, so `user_id` stops being null everywhere.</summary>
    [Fact]
    public async Task Login_WithCorrectPassword_WritesARecordNamingTheAccountAsync()
    {
        // Arrange
        const string email = "named-login@example.com";
        await RegisterAndVerifyAsync(email);

        // Act
        using HttpResponseMessage response = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AuditRecord record = Assert.Single(await RecordsForAsync("auth.login"));
        Assert.Equal("completed", record.Outcome);
        Assert.NotNull(record.UserId);
        Assert.Equal(record.UserId, record.TargetId);
    }

    /// <summary>
    /// Replaying a rotated refresh token is the loudest row the table holds: the family was revoked, and the
    /// row names whose — SDD §13.1.
    /// </summary>
    [Fact]
    public async Task Rotate_WithAlreadyRotatedToken_WritesAReuseDetectedRecordNamingTheAccountAsync()
    {
        // Arrange
        const string email = "reuse-audited@example.com";
        await RegisterAndVerifyAsync(email);

        TokenPair pair = await SignInAsync(email);
        using HttpResponseMessage rotated = await PostRotationAsync(pair.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        // Act — the spent value, presented again.
        using HttpResponseMessage replayed = await PostRotationAsync(pair.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        IReadOnlyList<AuditRecord> records = await RecordsForAsync("auth.rotate-session");
        AuditRecord reuse = Assert.Single(records, record => record.Outcome == "reusedetected");
        Assert.NotNull(reuse.UserId);
    }

    /// <summary>An unknown token is refused without naming an account that may not exist.</summary>
    [Fact]
    public async Task Rotate_WithAnUnknownToken_WritesARefusedRecordAsync()
    {
        // Act
        using HttpResponseMessage response = await PostRotationAsync(new string('z', 43));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        AuditRecord record = Assert.Single(await RecordsForAsync("auth.rotate-session"));
        Assert.Equal("refused", record.Outcome);
        Assert.Null(record.UserId);
    }

    /// <summary>An expired reset token is recorded as expired, not merely refused — ADR-0024.</summary>
    [Fact]
    public async Task Reset_WithAnExpiredToken_WritesAnExpiredRecordAsync()
    {
        // Arrange
        const string email = "expired-audited@example.com";
        await RegisterAndVerifyAsync(email);
        string token = await RequestResetAsync(email);
        await ExpireResetTokensOfAsync(email);

        // Act
        using HttpResponseMessage response = await PostAsync(
            PasswordResetEndpointTests.ResetsPath,
            new { token, newPassword = PasswordResetEndpointTests.NewPassword });

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("expired", Assert.Single(await RecordsForAsync("auth.reset-password")).Outcome);
    }

    /// <summary>
    /// A duplicate registration stays `completed`. A `refused` row here would tell anyone who can read the
    /// table which addresses already hold an account — SPEC-009 step 6.
    /// </summary>
    [Fact]
    public async Task Register_ForADuplicateAddress_WritesACompletedRecordAsync()
    {
        // Arrange
        const string email = "duplicate-audited@example.com";
        using HttpResponseMessage first = await PostRegistrationAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Act
        using HttpResponseMessage second = await PostRegistrationAsync(email);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        IReadOnlyList<AuditRecord> records = await RecordsForAsync("auth.register");
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal("completed", record.Outcome));
    }

    private async Task RegisterAndVerifyAsync(string email)
    {
        using HttpResponseMessage registered = await PostRegistrationAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);

        using HttpResponseMessage verified = await PostAsync(
            LoginEndpointTests.VerificationsPath,
            new { token = _mail.Tokens[^1] });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    private async Task<string> RequestResetAsync(string email)
    {
        int before = _mail.ResetTokens.Count;

        using HttpResponseMessage response = await PostAsync(
            PasswordResetEndpointTests.RequestsPath,
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, _mail.ResetTokens.Count);

        return _mail.ResetTokens[^1];
    }

    private async Task<TokenPair> SignInAsync(string email)
    {
        using HttpResponseMessage response = await PostLoginAsync(email, LoginEndpointTests.AccountPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenPair? pair = await response.Content.ReadFromJsonAsync<TokenPair>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(pair);

        return pair;
    }

    private async Task<HttpResponseMessage> PostRotationAsync(string refreshToken) =>
        await PostAsync(SessionRotationEndpointTests.TokensPath, new { refreshToken });

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
    }

    private async Task ExpireResetTokensOfAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        await context.PasswordResetTokens
            .Where(token => context.Users.Any(user => user.Id == token.UserId && user.Email == email))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)),
                TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostRegistrationAsync(string email)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            LoginEndpointTests.RegistrationPath,
            new { email, password = LoginEndpointTests.AccountPassword, displayName = "Player One" },
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostVerificationRequestAsync(string email)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            "/api/v1/auth/verification-requests",
            new { email },
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostLoginAsync(
        string email,
        string password = LoginEndpointTests.AccountPassword)
    {
        using HttpClient client = _factory.CreateAuditedClient();

        return await client.PostAsJsonAsync(
            LoginEndpointTests.SessionsPath,
            new { email, password },
            TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<AuditRecord>> RecordsForAsync(string action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .Where(record => record.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<AuditRecord>> AllRecordsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Records that mention an address anywhere. The expectation is none: the table stores identifiers, and
    /// this is the query that would find a leak.
    /// </summary>
    private async Task<IReadOnlyList<AuditRecord>> RecordsForEmailAsync(string email)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        return await context.AuditRecords
            .AsNoTracking()
            .Where(record => record.Action.Contains(email) || record.TargetType.Contains(email))
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
