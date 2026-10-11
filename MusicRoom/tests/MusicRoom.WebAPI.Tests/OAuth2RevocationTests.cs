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
/// Revocation — RFC 7009, SPEC-045.
/// </summary>
/// <remarks>
/// Two properties, and they pull in opposite directions. A revocation must actually end the session, and it
/// must tell a caller nothing about tokens it does not hold — so "it worked" and "there was nothing to do"
/// are the same `200`, and the only way to prove the first is to try the session afterwards.
/// </remarks>
public sealed class OAuth2RevocationTests : IAsyncLifetime
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

    /// <summary>Revoking a refresh token ends the session it belongs to.</summary>
    [Fact]
    public async Task ARevokedToken_CannotRotateAsync()
    {
        // Arrange
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "revoke-session@example.com");
        issued.Dispose();

        // Act
        using HttpResponseMessage revoked = await OAuth2Driver.RevokeAsync(_factory, pair.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using HttpResponseMessage rotation = await RefreshAsync(pair.RefreshToken);

        await OAuth2Checks.AssertErrorAsync(rotation, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>It ends the whole family, not the one row presented — ADR-0032, decision 5.</summary>
    /// <remarks>
    /// The test that would catch a revocation stamping a single row: the client holds the **rotated** token
    /// by then, and a revocation that left it alive would have revoked nothing that matters.
    /// </remarks>
    [Fact]
    public async Task RevokingOneToken_EndsTheWholeFamilyAsync()
    {
        // Arrange — rotate once, so two tokens of one family have existed.
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "revoke-family@example.com");
        issued.Dispose();

        using HttpResponseMessage rotated = await RefreshAsync(pair.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await rotated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string live = body.RootElement.GetProperty("refresh_token").GetString()!;

        // Act — revoke the live one.
        using HttpResponseMessage revoked = await OAuth2Driver.RevokeAsync(_factory, live);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        // Assert
        using HttpResponseMessage afterwards = await RefreshAsync(live);

        await OAuth2Checks.AssertErrorAsync(afterwards, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>An unknown token answers `200` and says nothing — RFC 7009 §2.2.</summary>
    /// <remarks>
    /// The alternative is an oracle: a `404` here would let anybody test whether a token they found is still
    /// a token.
    /// </remarks>
    [Fact]
    public async Task AnUnknownToken_Answers200Async()
    {
        using HttpResponseMessage response = await OAuth2Driver.RevokeAsync(
            _factory, OAuth2Checks.Verifier());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Revoking twice answers `200` twice, for the same reason.</summary>
    [Fact]
    public async Task ASecondRevocation_Answers200Async()
    {
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "revoke-twice@example.com");
        issued.Dispose();

        using HttpResponseMessage first = await OAuth2Driver.RevokeAsync(_factory, pair.RefreshToken);
        using HttpResponseMessage second = await OAuth2Driver.RevokeAsync(_factory, pair.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>The hint a client may send is accepted when it is the truth.</summary>
    [Fact]
    public async Task ARefreshTokenHint_IsAcceptedAsync()
    {
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "revoke-hint@example.com");
        issued.Dispose();

        using HttpResponseMessage revoked = await OAuth2Driver.RevokeAsync(
            _factory, pair.RefreshToken, "refresh_token");

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using HttpResponseMessage rotation = await RefreshAsync(pair.RefreshToken);

        await OAuth2Checks.AssertErrorAsync(rotation, HttpStatusCode.BadRequest, "invalid_grant");
    }

    /// <summary>An access token is refused rather than cheerfully accepted — decision 7.</summary>
    /// <remarks>
    /// A 15-minute HMAC JWT is not revocable by this design. Answering `200` would tell a client its access
    /// token is dead while it still opens every route in the API, which is worse than saying no.
    /// </remarks>
    [Fact]
    public async Task AnAccessTokenHint_IsRefusedAsync()
    {
        (HttpClient issued, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "revoke-access@example.com");
        issued.Dispose();

        using HttpResponseMessage response = await OAuth2Driver.RevokeAsync(
            _factory, pair.AccessToken, "access_token");

        await OAuth2Checks.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "unsupported_token_type");

        // And the session it belongs to is untouched, which is the honest part of the refusal.
        using HttpResponseMessage rotation = await RefreshAsync(pair.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        OAuth2Driver.PostAsync(
            _factory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });
}
