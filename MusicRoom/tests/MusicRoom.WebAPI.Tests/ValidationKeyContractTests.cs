using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Every key in a `422` is a field the route publishes — ADR-0043, SPEC-037, closing issue O28.
/// </summary>
/// <remarks>
/// The property, not a list of expected strings: each case reads the keys out of a real refusal and looks every
/// one of them up in that route's request schema in `docs/api/openapi.json`. A route added tomorrow with a
/// PascalCase key, or a rule that leaks a CLR member like `genres.count`, fails this without anybody remembering
/// to extend a list. The twenty routes keep their own key assertions from SPEC-036; this is what catches the
/// twenty-first.
/// <para>
/// One case per shape rather than per route: a flat body, a nested object, a body the generator wraps in
/// `oneOf: [null, …]`, a query string, an array whose rule names the array, and a rule over the whole command
/// that would otherwise publish the empty key.
/// </para>
/// </remarks>
public sealed class ValidationKeyContractTests : IAsyncLifetime
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

    /// <summary>A flat JSON body: every key is a property of the request schema.</summary>
    [Fact]
    public async Task Registration_KeysAreFieldsOfItsRequestSchemaAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationEndpointTests.RegistrationsPath,
            new { email = "schema-flat@example.com", password = "short", displayName = "Player One" },
            TestContext.Current.CancellationToken);

        // Assert
        await AssertKeysAsync(response, "/api/v1/auth/registrations", "post");
    }

    /// <summary>A query string: the keys are parameter names, which is where `q` lives.</summary>
    [Fact]
    public async Task TrackSearch_KeysAreItsQueryParametersAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("schema-query@example.com");

        // Act
        using HttpResponseMessage response = await caller.GetAsync(
            new Uri("/api/v1/tracks", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert — `q`, not `query`: the property is named differently from the field.
        await AssertKeysAsync(response, "/api/v1/tracks", "get");
    }

    /// <summary>A nested object: `bio.value` resolves because the field really carries a value.</summary>
    [Fact]
    public async Task ProfileUpdate_KeysAreDottedPathsThatResolveAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("schema-nested@example.com");

        // Act
        using HttpResponseMessage response = await caller.PatchAsJsonAsync(
            ProfileEndpointTests.ProfilePath,
            new { bio = new { value = new string('b', 513), visibility = ProfileFieldVisibility.Public } },
            TestContext.Current.CancellationToken);

        // Assert
        await AssertKeysAsync(response, "/api/v1/me/profile", "patch");
    }

    /// <summary>An array whose rule counts it: the key is the array, never `genres.count`.</summary>
    [Fact]
    public async Task MusicPreferences_KeysNameTheArrayAndNotItsCountAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("schema-array@example.com");
        string[] genres = [.. Enumerable.Range(0, 200).Select(index => $"genre-{index}")];

        // Act
        using HttpResponseMessage response = await caller.PutAsJsonAsync(
            ProfileEndpointTests.PreferencesPath,
            new { genres, artists = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        // Assert
        await AssertKeysAsync(response, "/api/v1/me/music-preferences", "put");
    }

    /// <summary>A nullable body, which the generator wraps in `oneOf: [null, …]`.</summary>
    [Fact]
    public async Task EventCode_KeysAreFieldsOfANullableBodyAsync()
    {
        // Arrange
        using HttpClient owner = await SignInAsync("schema-nullable@example.com");
        Guid eventId = await EventRequests.CreateAsync(owner, "Schema");

        // Act — a day, which the validator refuses.
        using HttpResponseMessage response = await EventQueueRequests.PostCodeAsync(owner, eventId, 86_400);

        // Assert — `ttlSeconds`, the field `EventCodeRequest` publishes, not the command's property name.
        await AssertKeysAsync(response, "/api/v1/events/{eventId}/event-codes", "post");
    }

    /// <summary>
    /// A rule over the whole command: it names a field, so the key is never the empty string.
    /// </summary>
    /// <remarks>
    /// This is the defect O28 named. Before SPEC-037 this body was `{"errors":{"":[…]}}`, and an empty key is
    /// not a field of anything — so this case fails on `main` before the override exists, which is what makes it
    /// a regression test rather than a description.
    /// </remarks>
    [Fact]
    public async Task MoveTrack_KeysNameAFieldRatherThanTheEmptyStringAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("schema-whole-command@example.com");
        Guid playlistId = await PlaylistEndpointTests.CreateAsync(caller, "Schema");
        Guid trackId = await TrackRequests.AddAsync(caller, playlistId, "aaaaaaaaaaa");

        // Act
        using HttpResponseMessage response = await TrackRequests.PatchTrackAsync(
            caller,
            playlistId,
            trackId,
            new { },
            await TrackRequests.ETagAsync(caller, playlistId));

        // Assert
        await AssertKeysAsync(
            response,
            "/api/v1/playlists/{playlistId}/tracks/{playlistTrackId}",
            "patch");
    }

    private static async Task AssertKeysAsync(HttpResponseMessage response, string routeTemplate, string method)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        string[] keys = await ValidationProblems.KeysAsync(response);

        ValidationProblems.AssertKeysAreRequestFields(routeTemplate, method, keys);
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        return client;
    }
}
