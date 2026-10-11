using System.Net;
using System.Text.Json;
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
/// UserInfo, and the scope rule it enforces — ADR-0031, SPEC-045.
/// </summary>
/// <remarks>
/// The claim-by-claim assertions are the point. A suite that only checked "some claims came back" would pass
/// while a provider returned everything to a client that asked for `openid`, which is the failure mode a
/// scope exists to prevent.
/// </remarks>
public sealed class OAuth2UserInfoTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, registers one client, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            TestRateLimitSettings.Relax(webHost);
            OAuth2Driver.Apply(webHost);
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

    /// <summary>`openid email` answers the address and withholds the name.</summary>
    [Fact]
    public async Task TheEmailScope_AnswersTheAddressAndNotTheNameAsync()
    {
        // Arrange
        const string email = "userinfo-email@example.com";

        (HttpClient granted, JsonDocument tokens) = await OAuth2Driver.GrantedAsync(
            _factory, _mail, email, "openid email");

        using HttpClient client = granted;
        tokens.Dispose();

        // Act
        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(client);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = await ReadAsync(response);

        Assert.NotEmpty(body.RootElement.GetProperty("sub").GetString()!);
        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.True(body.RootElement.GetProperty("email_verified").GetBoolean());

        // The scope was not asked for, so the claim is absent rather than null — a member present with no
        // value is a member a client has to guess about.
        Assert.False(body.RootElement.TryGetProperty("name", out _));
    }

    /// <summary>`openid` alone answers the subject and nothing else.</summary>
    /// <remarks>
    /// The narrowest grant this provider issues, and the test that would fail if the handler stopped
    /// filtering: everything else in the claim set exists in the database for this account.
    /// </remarks>
    [Fact]
    public async Task TheOpenidScopeAlone_AnswersOnlyTheSubjectAsync()
    {
        (HttpClient granted, JsonDocument tokens) = await OAuth2Driver.GrantedAsync(
            _factory, _mail, "userinfo-minimal@example.com", "openid");

        using HttpClient client = granted;
        tokens.Dispose();

        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(client);

        using JsonDocument body = await ReadAsync(response);

        Assert.NotEmpty(body.RootElement.GetProperty("sub").GetString()!);
        Assert.False(body.RootElement.TryGetProperty("email", out _));
        Assert.False(body.RootElement.TryGetProperty("email_verified", out _));
        Assert.False(body.RootElement.TryGetProperty("name", out _));
    }

    /// <summary>`openid profile email` answers all three.</summary>
    [Fact]
    public async Task EveryScope_AnswersEveryClaimAsync()
    {
        (HttpClient granted, JsonDocument tokens) = await OAuth2Driver.GrantedAsync(
            _factory, _mail, "userinfo-everything@example.com", "openid profile email");

        using HttpClient client = granted;
        tokens.Dispose();

        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(client);

        using JsonDocument body = await ReadAsync(response);

        Assert.Equal("userinfo-everything@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.NotEmpty(body.RootElement.GetProperty("name").GetString()!);
    }

    /// <summary>A session from the credential endpoints granted nothing and receives everything.</summary>
    /// <remarks>
    /// SPEC-045 decision 2, asserted rather than assumed. Null scope is not an empty grant: it is the
    /// account's own client, which `GET /me` already answers with strictly more.
    /// </remarks>
    [Fact]
    public async Task AFirstPartySession_ReceivesTheFullClaimSetAsync()
    {
        const string email = "userinfo-first-party@example.com";

        using HttpClient signedIn = await OAuth2Driver.SignedInAsync(_factory, _mail, email);

        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(signedIn);

        using JsonDocument body = await ReadAsync(response);

        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.True(body.RootElement.GetProperty("email_verified").GetBoolean());
        Assert.NotEmpty(body.RootElement.GetProperty("name").GetString()!);
    }

    /// <summary>A rotation continues the grant rather than widening it.</summary>
    /// <remarks>
    /// The column is inherited by every replacement for this reason. Without the inheritance a narrow grant
    /// would become a full one after one refresh, which is the quietest way for a scope rule to stop
    /// existing.
    /// </remarks>
    [Fact]
    public async Task ARotation_DoesNotWidenTheGrantAsync()
    {
        // Arrange — a narrow grant, then a refresh.
        (HttpClient granted, JsonDocument tokens) = await OAuth2Driver.GrantedAsync(
            _factory, _mail, "userinfo-rotation@example.com", "openid");

        granted.Dispose();

        using HttpResponseMessage rotated = await OAuth2Driver.PostAsync(
            _factory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = tokens.RootElement.GetProperty("refresh_token").GetString()!,
            });

        tokens.Dispose();

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using JsonDocument refreshed = await ReadAsync(rotated);

        using HttpClient client = _factory.CreateAuditedClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            refreshed.RootElement.GetProperty("access_token").GetString());

        // Act
        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(client);

        // Assert — still `openid` only.
        using JsonDocument body = await ReadAsync(response);

        Assert.NotEmpty(body.RootElement.GetProperty("sub").GetString()!);
        Assert.False(body.RootElement.TryGetProperty("email", out _));
    }

    /// <summary>Without a token there is no subject to answer — OpenID Connect Core 1.0 §5.3.</summary>
    [Fact]
    public async Task AnAnonymousRequest_Is401Async()
    {
        using HttpClient anonymous = _factory.CreateClient();

        using HttpResponseMessage response = await OAuth2Driver.UserInfoAsync(anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
