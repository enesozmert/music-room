using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Validation;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Holds the `codes` member to ADR-0045 — SPEC-038.
/// </summary>
/// <remarks>
/// Three things a client depends on and nothing else asserts. That `codes` exists beside `errors` with the
/// same keys, because a client reads one and renders the other. That a handler refusal, which has no field to
/// key on, carries the single `code` member instead of an empty dictionary. And that no key anywhere is the
/// empty string — ADR-0043's rule, which SPEC-037 applied to one validator and missed in another.
/// <para>
/// The codes themselves are asserted by name rather than by shape. A code is a contract: a test that only
/// checked "some code is present" would pass while a rename silently broke every client that switched on the
/// old one.
/// </para>
/// </remarks>
public sealed class ValidationCodeContractTests : IAsyncLifetime
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

    /// <summary>A validation failure carries its codes beside its messages, under the same keys.</summary>
    [Fact]
    public async Task AValidationFailure_CarriesCodesUnderTheSameKeysAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act — a password under the minimum and an address that is not one.
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationEndpointTests.RegistrationsPath,
            new { email = "not-an-address", password = "short", displayName = "Player One" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        string[] keys = await ValidationProblems.KeysAsync(response);
        Dictionary<string, string[]> codes = await ValidationProblems.CodesAsync(response);

        Assert.Equal<IEnumerable<string>>(keys, [.. codes.Keys]);
        Assert.Equal<IEnumerable<string>>([ValidationCodes.EmailInvalid], codes["email"]);
        Assert.Equal<IEnumerable<string>>([ValidationCodes.PasswordTooShort], codes["password"]);
    }

    /// <summary>One message, one code, in the same position under the key.</summary>
    /// <remarks>
    /// The pairing is what a client acts on: it shows the message and branches on the code beside it. Built by
    /// one function in one pass so they cannot drift, and this is the assertion that keeps that true.
    /// </remarks>
    [Fact]
    public async Task EachKey_HasAsManyCodesAsMessagesAsync()
    {
        // Arrange
        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RegistrationEndpointTests.RegistrationsPath,
            new { email = string.Empty, password = string.Empty, displayName = string.Empty },
            TestContext.Current.CancellationToken);

        // Assert
        Dictionary<string, string[]> codes = await ValidationProblems.CodesAsync(response);

        foreach ((string key, string[] keyCodes) in codes)
        {
            string[] messages = await ValidationProblems.MessagesAsync(response, key);

            Assert.Equal(messages.Length, keyCodes.Length);
        }
    }

    /// <summary>No key is ever the empty string — ADR-0043, on the route SPEC-037 missed.</summary>
    /// <remarks>
    /// `AddTrackCommandValidator`'s "atHead or afterTrackId, not both" rule had no member expression and no
    /// override, so it published `{"errors":{"":[…]}}` — a message a client cannot route to a field. The sibling
    /// rule in `MoveTrackCommandValidator` was fixed by SPEC-037 and this one was not, which is why the
    /// assertion is here rather than in a comment.
    /// </remarks>
    [Fact]
    public async Task AWholeCommandRule_NamesAFieldRatherThanNothingAsync()
    {
        // Arrange
        using HttpClient caller = await SignInAsync("codes-whole-command@example.com");

        Guid playlistId = await PlaylistEndpointTests.CreateAsync(caller, "Codes");

        string etag = await TrackRequests.ETagAsync(caller, playlistId);

        // Act — both destinations at once, which the rule refuses.
        using HttpResponseMessage response = await TrackRequests.PostTrackAsync(
            caller,
            playlistId,
            new { youtubeVideoId = "dQw4w9WgXcQ", atHead = true, afterTrackId = Guid.NewGuid() },
            etag);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        string[] keys = await ValidationProblems.KeysAsync(response);

        Assert.DoesNotContain(string.Empty, keys);
        Assert.Contains("atHead", keys);

        Dictionary<string, string[]> codes = await ValidationProblems.CodesAsync(response);

        Assert.Equal<IEnumerable<string>>([ValidationCodes.PositionConflicting], codes["atHead"]);
    }

    private async Task<HttpClient> SignInAsync(string email)
    {
        (HttpClient client, TokenPair _) = await TestBearer.SignInAsync(_factory, _mail, email);

        return client;
    }
}
