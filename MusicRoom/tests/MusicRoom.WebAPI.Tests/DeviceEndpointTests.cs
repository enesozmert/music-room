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
/// Device registration, the list and the removal — SPEC-020, subject V.2.2.
/// </summary>
/// <remarks>
/// The test that carries a decision is `Register_AfterAnAppUpdate_IsTheSameDevice`: a phone that updates its app
/// is the same phone, which is why `app_version` is not part of the unique key.
/// </remarks>
public sealed class DeviceEndpointTests : IAsyncLifetime
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
            TestMailSettings.Apply(webHost);
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

    /// <summary>A registered device comes back in the list, carrying the header values.</summary>
    [Fact]
    public async Task Register_ThenList_ReturnsTheDeviceAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-owner@example.com");

        using HttpClient a = owner;

        // Act
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Assert
        DeviceRequests.DeviceBody only = Assert.Single(await DeviceRequests.ListAsync(a));

        Assert.Equal(deviceId, only.DeviceId);
        Assert.Null(only.ControllerUserId);

        // The three values came from the mandatory client headers, not from a body — decision 7.
        Assert.False(string.IsNullOrWhiteSpace(only.Platform));
        Assert.False(string.IsNullOrWhiteSpace(only.Model));
        Assert.False(string.IsNullOrWhiteSpace(only.AppVersion));
    }

    /// <summary>The same device twice is `409`, decided by the unique index.</summary>
    [Fact]
    public async Task Register_TheSameDeviceTwice_Returns409Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-duplicate@example.com");

        using HttpClient a = owner;
        await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage second = await DeviceRequests.PostAsync(a);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await DeviceRequests.ListAsync(a));
    }

    /// <summary>
    /// A phone that updates its app is the same phone — `app_version` is not part of the key (decision 3).
    /// </summary>
    [Fact]
    public async Task Register_AfterAnAppUpdate_IsTheSameDeviceAsync()
    {
        // Arrange
        (HttpClient owner, TokenPair pair) = await TestBearer.SignInAsync(
            _factory, _mail, "device-updated@example.com");

        using HttpClient a = owner;
        await DeviceRequests.RegisterAsync(a);

        // A second client with the same platform and device, and a newer app version.
        using HttpClient updated = _factory.CreateAuditedClient(appVersion: "9.9.9");
        updated.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.AccessToken);

        // Act
        using HttpResponseMessage response = await DeviceRequests.PostAsync(updated);

        // Assert — refused as a duplicate, and still one row.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await DeviceRequests.ListAsync(a));
    }

    /// <summary>The list is the caller's own and nobody else's.</summary>
    [Fact]
    public async Task List_ReturnsOnlyTheCallersDevicesAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-mine@example.com");
        (HttpClient other, _) = await TestBearer.SignInAsync(_factory, _mail, "device-theirs@example.com");

        using HttpClient a = owner;
        using HttpClient b = other;

        Guid mine = await DeviceRequests.RegisterAsync(a);
        Guid theirs = await DeviceRequests.RegisterAsync(b);

        // Act
        IReadOnlyList<DeviceRequests.DeviceBody> listed = await DeviceRequests.ListAsync(a);

        // Assert
        Assert.Equal(mine, Assert.Single(listed).DeviceId);
        Assert.NotEqual(mine, theirs);
    }

    /// <summary>Another account's device cannot be removed, and the answer is `404`.</summary>
    [Fact]
    public async Task Remove_AnotherAccountsDevice_Returns404Async()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-victim@example.com");
        (HttpClient stranger, _) = await TestBearer.SignInAsync(_factory, _mail, "device-thief@example.com");

        using HttpClient a = owner;
        using HttpClient b = stranger;

        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await b.DeleteAsync(
            $"{DeviceRequests.DevicesPath}/{deviceId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await DeviceRequests.ListAsync(a));
    }

    /// <summary>The owner may remove their own, and it leaves the list.</summary>
    [Fact]
    public async Task Remove_AsTheOwner_SucceedsAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-removed@example.com");

        using HttpClient a = owner;
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Act
        using HttpResponseMessage response = await a.DeleteAsync(
            $"{DeviceRequests.DevicesPath}/{deviceId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await DeviceRequests.ListAsync(a));
    }

    /// <summary>Registering a device is audited — ADR-0033 on a new surface.</summary>
    [Fact]
    public async Task Register_WritesAnAuditRecordAsync()
    {
        // Arrange
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, "device-audited@example.com");

        using HttpClient a = owner;

        // Act
        Guid deviceId = await DeviceRequests.RegisterAsync(a);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Action == "device.register",
                TestContext.Current.CancellationToken);

        Assert.Equal(deviceId, record.TargetId);
        Assert.Equal("device", record.TargetType);
    }
}
