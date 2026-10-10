using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Identity;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Application.Validation;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Sign in with a provider, and the three link routes — subject V.1, SPEC-041.
/// </summary>
/// <remarks>
/// The relying-party core is substituted, not the HTTP transport underneath it: what these tests are about is
/// what this API does **after** a provider has authenticated somebody, and
/// `OpenIdConnectIdentityProviderTests` is where the six checks live. Driving a real token through here would
/// test the same validation twice and leave the account rules untested.
/// </remarks>
public sealed class SocialAuthEndpointTests : IAsyncLifetime
{
    private const string _socialSessionsPath = "/api/v1/auth/sessions/social";
    private const string _socialLinksPath = "/api/v1/auth/social-links";
    private const string _provider = "google";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();
    private readonly StubSocialIdentityProvider _identities = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, substitutes the mail sender and the relying-party core.</summary>
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
            {
                services.AddSingleton<IMailService>(_mail);
                services.AddSingleton<ISocialIdentityProvider>(_identities);
            });
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

    /// <summary>A provider identity nobody has seen creates an account and signs in — subject V.1.</summary>
    [Fact]
    public async Task AFirstSignIn_CreatesTheAccountAsync()
    {
        // Arrange
        _identities.Asserted = Identity("first-sign-in", "first@example.com");

        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await SignInAsync(client);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        Domain.Entities.User user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == "first@example.com", TestContext.Current.CancellationToken);

        // The account the provider created has no password, and is verified because the provider proved
        // the address — SPEC-041, the owner's answer 2.
        Assert.Null(user.PasswordHash);
        Assert.True(user.IsEmailVerified);
    }

    /// <summary>The same identity signs into the same account rather than making a second one.</summary>
    [Fact]
    public async Task ASecondSignIn_ReusesTheAccountAsync()
    {
        // Arrange
        _identities.Asserted = Identity("returning", "returning@example.com");

        using HttpClient client = _factory.CreateAuditedClient();

        using HttpResponseMessage first = await SignInAsync(client);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using HttpResponseMessage second = await SignInAsync(client);

        // Assert
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();

        int accounts = await context.Users
            .CountAsync(candidate => candidate.Email == "returning@example.com", TestContext.Current.CancellationToken);

        Assert.Equal(1, accounts);
    }

    /// <summary>An address the provider has not verified is refused — ADR-0008's takeover path.</summary>
    [Fact]
    public async Task AnUnverifiedAddress_IsRefusedAsync()
    {
        // Arrange
        _identities.Asserted = Identity("unverified", "unverified@example.com") with { IsEmailVerified = false };

        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await SignInAsync(client);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ValidationCodes.SocialEmailUnverified, await ValidationProblems.CodeAsync(response));
    }

    /// <summary>A provider nobody configured is a client mistake, said plainly.</summary>
    [Fact]
    public async Task AnUnknownProvider_IsRefusedWithItsCodeAsync()
    {
        // Arrange
        _identities.Outcome = SocialAuthenticationOutcome.UnknownProvider;

        using HttpClient client = _factory.CreateAuditedClient();

        // Act
        using HttpResponseMessage response = await SignInAsync(client, provider: "nobody");

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ValidationCodes.ProviderUnknown, await ValidationProblems.CodeAsync(response));
    }

    /// <summary>A password account links a provider, and sees it in its own list — subject V.1's second row.</summary>
    [Fact]
    public async Task AnAccount_LinksAProviderAndListsItAsync()
    {
        // Arrange
        (HttpClient caller, TokenPair _) = await TestBearer.SignInAsync(
            _factory, _mail, "links@example.com");

        using HttpClient client = caller;

        _identities.Asserted = Identity("linked", "links@example.com");

        // Act
        using HttpResponseMessage linked = await client.PostAsJsonAsync(
            new Uri(_socialLinksPath, UriKind.Relative),
            Payload(_provider),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);

        SocialLinkBody[]? links = await client.GetFromJsonAsync<SocialLinkBody[]>(
            new Uri(_socialLinksPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotNull(links);
        Assert.Equal(_provider, Assert.Single(links).Provider);
    }

    /// <summary>An identity that already signs somebody in cannot be linked again.</summary>
    [Fact]
    public async Task AnIdentityLinkedElsewhere_AnswersConflictAsync()
    {
        // Arrange — the identity signs itself in first, which links it to the account it created.
        _identities.Asserted = Identity("taken", "taken@example.com");

        using HttpClient anonymous = _factory.CreateAuditedClient();
        using HttpResponseMessage signedIn = await SignInAsync(anonymous);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        (HttpClient caller, TokenPair _) = await TestBearer.SignInAsync(
            _factory, _mail, "somebody-else@example.com");

        using HttpClient client = caller;

        // Act
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(_socialLinksPath, UriKind.Relative),
            Payload(_provider),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ValidationCodes.SocialAlreadyLinked, await ValidationProblems.CodeAsync(response));
    }

    /// <summary>
    /// The only way into an account stays — an account with no password and one link, SPEC-041.
    /// </summary>
    [Fact]
    public async Task TheLastSignInMethod_CannotBeUnlinkedAsync()
    {
        // Arrange — a social-only account, which is one link and no password.
        _identities.Asserted = Identity("only-way-in", "only-way-in@example.com");

        using HttpClient anonymous = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await SignInAsync(anonymous);

        TokenPairBody? pair = await response.Content.ReadFromJsonAsync<TokenPairBody>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(pair);

        using HttpClient client = _factory.CreateAuditedClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);

        // Act
        using HttpResponseMessage refused = await client.DeleteAsync(
            new Uri($"{_socialLinksPath}/{_provider}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ValidationCodes.SocialLastSignInMethod, await ValidationProblems.CodeAsync(refused));
    }

    /// <summary>
    /// A social-only account cannot change a password it never had — `422`, not `401`.
    /// </summary>
    /// <remarks>
    /// The same refusal a wrong current password takes, deliberately: an account with no password has nothing
    /// to replace, and saying so differently would tell a caller which accounts have one. Setting a first
    /// password is the reset flow, which proves the address.
    /// </remarks>
    [Fact]
    public async Task ASocialOnlyAccount_CannotChangeItsPasswordAsync()
    {
        // Arrange
        _identities.Asserted = Identity("no-password", "no-password@example.com");

        using HttpClient anonymous = _factory.CreateAuditedClient();
        using HttpResponseMessage response = await SignInAsync(anonymous);

        TokenPairBody? pair = await response.Content.ReadFromJsonAsync<TokenPairBody>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(pair);

        using HttpClient client = _factory.CreateAuditedClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", pair.AccessToken);

        // Act
        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            new Uri("/api/v1/me/password", UriKind.Relative),
            new { currentPassword = "whatever it might be", newPassword = "a replacement passphrase" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string provider = _provider) =>
        client.PostAsJsonAsync(
            new Uri(_socialSessionsPath, UriKind.Relative),
            Payload(provider),
            TestContext.Current.CancellationToken);

    private static object Payload(string provider) =>
        new
        {
            provider,
            code = "an-authorization-code",
            codeVerifier = "a-verifier-long-enough-to-be-one",
            redirectUri = "https://app.example.com/callback",
        };

    private static SocialIdentity Identity(string subject, string email) =>
        new(_provider, subject, email, IsEmailVerified: true, DisplayName: "A Person");

    /// <summary>The token pair, as a client reads it.</summary>
    /// <param name="AccessToken">The bearer token.</param>
    private sealed record TokenPairBody(string AccessToken);

    /// <summary>One link, as a client reads it.</summary>
    /// <param name="Provider">Which provider.</param>
    private sealed record SocialLinkBody(string Provider);

    /// <summary>
    /// Answers whatever the test set, without reaching a provider.
    /// </summary>
    /// <remarks>
    /// The six checks are `OpenIdConnectIdentityProviderTests`'s subject. Here the question is what this API
    /// does with an identity once a provider has asserted it, which is a different test and deserves a
    /// different double.
    /// </remarks>
    private sealed class StubSocialIdentityProvider : ISocialIdentityProvider
    {
        /// <summary>What the next authentication yields.</summary>
        internal SocialIdentity? Asserted { get; set; }

        /// <summary>What the next authentication answers when it is not authenticated.</summary>
        internal SocialAuthenticationOutcome Outcome { get; set; } = SocialAuthenticationOutcome.Authenticated;

        /// <inheritdoc />
        public Task<SocialAuthentication> AuthenticateAsync(
            SocialAuthenticationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Outcome == SocialAuthenticationOutcome.Authenticated && Asserted is { } identity
                ? SocialAuthentication.Of(identity)
                : SocialAuthentication.Refused(Outcome));
    }
}
