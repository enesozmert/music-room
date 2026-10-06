using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The caller's own profile end to end — SPEC-011, subject V.1.
/// </summary>
public sealed class ProfileEndpointTests : IAsyncLifetime
{
    internal const string MePath = "/api/v1/me/";
    internal const string ProfilePath = "/api/v1/me/profile";
    internal const string PreferencesPath = "/api/v1/me/music-preferences";

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

    /// <summary>The `self` gate: no token, no profile.</summary>
    [Fact]
    public async Task GetOwnProfile_WithoutAToken_Returns401Async()
    {
        // Arrange
        using HttpClient anonymous = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await anonymous.GetAsync(
            MePath,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A freshly registered account has a profile: empty fields, the default tiers, and the display name it
    /// registered with — SPEC-011 escalation 4, so no read path has a null case.
    /// </summary>
    [Fact]
    public async Task GetOwnProfile_ForAFreshAccount_ReturnsAnEmptyProfileNotA404Async()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "fresh-profile@example.com");

        using HttpClient authenticated = client;

        // Act
        ProfileBody? profile = await ReadProfileAsync(authenticated);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal("Player One", profile.DisplayName);
        Assert.Null(profile.RealName.Value);
        Assert.Equal(ProfileFieldVisibility.Friends, profile.RealName.Visibility);
        Assert.Equal(ProfileFieldVisibility.Public, profile.Bio.Visibility);
        Assert.Equal(ProfileFieldVisibility.Private, profile.BirthDate.Visibility);
        Assert.Empty(profile.Genres);
        Assert.Empty(profile.Artists);
    }

    /// <summary>Every field comes back with the tier that governs it — subject V.1's three tiers.</summary>
    [Fact]
    public async Task UpdateProfile_ThenRead_ReturnsEveryFieldWithItsVisibilityAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "fills-profile@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage updated = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new
            {
                realName = new { value = "Ada Lovelace", visibility = ProfileFieldVisibility.Friends },
                bio = new { value = "counts on machines", visibility = ProfileFieldVisibility.Public },

                // Inside the validator's 1900 floor, which exists to catch a bad parse rather than to insult
                // anybody: a date of 1815 is refused, as the validator's own test asserts.
                birthDate = new { value = "1990-12-10", visibility = ProfileFieldVisibility.Private },
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        ProfileBody? profile = await ReadProfileAsync(authenticated);
        Assert.NotNull(profile);
        Assert.Equal("Ada Lovelace", profile.RealName.Value);
        Assert.Equal(ProfileFieldVisibility.Friends, profile.RealName.Visibility);
        Assert.Equal("counts on machines", profile.Bio.Value);
        Assert.Equal(new DateOnly(1990, 12, 10), profile.BirthDate.Value);
        Assert.Equal(ProfileFieldVisibility.Private, profile.BirthDate.Visibility);
    }

    /// <summary>A PATCH touches what it names and nothing else.</summary>
    [Fact]
    public async Task UpdateProfile_WithASubset_LeavesTheOtherFieldsUntouchedAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "subset@example.com");

        using HttpClient authenticated = client;

        using HttpResponseMessage first = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new
            {
                realName = new { value = "Kept", visibility = ProfileFieldVisibility.Friends },
                bio = new { value = "also kept", visibility = ProfileFieldVisibility.Public },
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act — only the location this time.
        using HttpResponseMessage second = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new { location = new { value = "London", visibility = ProfileFieldVisibility.Private } },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        ProfileBody? profile = await ReadProfileAsync(authenticated);
        Assert.NotNull(profile);
        Assert.Equal("Kept", profile.RealName.Value);
        Assert.Equal("also kept", profile.Bio.Value);
        Assert.Equal("London", profile.Location.Value);
        Assert.Equal(ProfileFieldVisibility.Private, profile.Location.Visibility);
    }

    /// <summary>
    /// A value with no tier is refused — `docs/api/ENDPOINTS.md`: "a missing tier is `422`, never a silent
    /// default to public".
    /// </summary>
    [Fact]
    public async Task UpdateProfile_WithAValueAndNoVisibility_IsRefusedAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "no-tier@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new { realName = new { value = "No Tier Given" } },
            TestContext.Current.CancellationToken);

        // Assert — refused, and nothing was stored.
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"expected a refusal, got {response.StatusCode}");

        ProfileBody? profile = await ReadProfileAsync(authenticated);
        Assert.NotNull(profile);
        Assert.Null(profile.RealName.Value);
    }

    /// <summary>A PUT replaces the whole preference set.</summary>
    [Fact]
    public async Task ReplaceMusicPreferences_ReplacesTheWholeSetAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "prefers@example.com");

        using HttpClient authenticated = client;

        using HttpResponseMessage first = await authenticated.PutAsJsonAsync(
            PreferencesPath,
            new { genres = new[] { "drum and bass", "dub" }, artists = new[] { "Goldie" } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using HttpResponseMessage second = await authenticated.PutAsJsonAsync(
            PreferencesPath,
            new { genres = new[] { "ambient" }, artists = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        ProfileBody? profile = await ReadProfileAsync(authenticated);
        Assert.NotNull(profile);
        Assert.Equal(["ambient"], profile.Genres);
        Assert.Empty(profile.Artists);
    }

    /// <summary>The same genre named twice is one preference, not a conflict.</summary>
    [Fact]
    public async Task ReplaceMusicPreferences_WithADuplicate_StoresItOnceAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "duplicates@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.PutAsJsonAsync(
            PreferencesPath,
            new { genres = new[] { "techno", "Techno", " techno " }, artists = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ProfileBody? profile = await ReadProfileAsync(authenticated);
        Assert.NotNull(profile);
        Assert.Single(profile.Genres);
    }

    /// <summary>An unbounded list is refused — OWASP API4.</summary>
    [Fact]
    public async Task ReplaceMusicPreferences_WithTooManyEntries_Returns422Async()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "too-many@example.com");

        using HttpClient authenticated = client;

        string[] genres = Enumerable.Range(0, 200).Select(index => $"genre-{index}").ToArray();

        // Act
        using HttpResponseMessage response = await authenticated.PutAsJsonAsync(
            PreferencesPath,
            new { genres, artists = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["genres"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>A bio past the cap is `422` keyed on `Bio.Value` — SPEC-036.</summary>
    /// <remarks>
    /// A deterministic refusal, deliberately: the other `422` in this suite accepts `400` **or** `422` because it
    /// sends a field with no visibility tier, which the binder may reject first. A key assertion needs one answer.
    /// The key is the path the rule names — `RuleFor(command => command.Bio!.Value)` — which is the same dotted
    /// shape `Genres.Count` has, and a client reads it as given.
    /// </remarks>
    [Fact]
    public async Task UpdateProfile_WithATooLongBio_Returns422KeyedOnBioValueAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "profile-long-bio@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new
            {
                bio = new { value = new string('b', 513), visibility = ProfileFieldVisibility.Public },
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(["bio.value"], await ValidationProblems.KeysAsync(response));
    }

    /// <summary>An authenticated mutation is audited, named and attributed — SPEC-009 and SPEC-010.</summary>
    [Fact]
    public async Task UpdateProfile_WritesAnAuditRecordNamingTheAccountAsync()
    {
        // Arrange
        (HttpClient client, _) = await TestBearer.SignInAsync(_factory, _mail, "audited-profile@example.com");

        using HttpClient authenticated = client;

        // Act
        using HttpResponseMessage response = await authenticated.PatchAsJsonAsync(
            ProfilePath,
            new { bio = new { value = "audited", visibility = ProfileFieldVisibility.Public } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert
        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        AuditRecord record = await context.AuditRecords
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Action == "profile.update", TestContext.Current.CancellationToken);

        Assert.NotNull(record.UserId);
        Assert.Equal(record.UserId, record.TargetId);
        Assert.Equal("profile", record.TargetType);
        Assert.Equal("completed", record.Outcome);
    }

    private static async Task<ProfileBody?> ReadProfileAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            MePath,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ProfileBody>(TestContext.Current.CancellationToken);
    }

    /// <summary>The profile body, as a client reads it.</summary>
    /// <param name="DisplayName">The always-public handle.</param>
    /// <param name="RealName">The real name and its tier.</param>
    /// <param name="Bio">The bio and its tier.</param>
    /// <param name="Location">The location and its tier.</param>
    /// <param name="BirthDate">The date of birth and its tier.</param>
    /// <param name="Genres">The genres named.</param>
    /// <param name="Artists">The artists named.</param>
    internal sealed record ProfileBody(
        string DisplayName,
        FieldBody<string> RealName,
        FieldBody<string> Bio,
        FieldBody<string> Location,
        DateFieldBody BirthDate,
        IReadOnlyList<string> Genres,
        IReadOnlyList<string> Artists);

    /// <summary>One string field and its tier.</summary>
    /// <typeparam name="T">The field's type.</typeparam>
    /// <param name="Value">The value, or null.</param>
    /// <param name="Visibility">Who may see it.</param>
    internal sealed record FieldBody<T>(T? Value, ProfileFieldVisibility Visibility);

    /// <summary>The date-of-birth field and its tier.</summary>
    /// <param name="Value">The date, or null.</param>
    /// <param name="Visibility">Who may see it.</param>
    internal sealed record DateFieldBody(DateOnly? Value, ProfileFieldVisibility Visibility);
}
