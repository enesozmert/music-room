using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What a standard client reads before it talks to this API — ADR-0031, SPEC-043.
/// </summary>
/// <remarks>
/// No database: these two routes read a key out of configuration and serialise. Starting a container for
/// them would be a minute of CI for nothing, and the suites that need one say why they do.
/// </remarks>
public sealed class OpenIdMetadataEndpointTests
{
    private static readonly string _pem = NewKeyPem();

    /// <summary>The discovery document carries what a client needs to find the keys.</summary>
    [Fact]
    public async Task TheDiscoveryDocument_NamesTheIssuerAndTheKeySetAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Act
        using JsonDocument document = await ReadAsync(client, "/.well-known/openid-configuration");

        // Assert
        string issuer = document.RootElement.GetProperty("issuer").GetString()!;

        Assert.StartsWith("http", issuer, StringComparison.Ordinal);
        Assert.Equal(issuer + "/.well-known/jwks.json", document.RootElement.GetProperty("jwks_uri").GetString());

        // RS256 and nothing else: ADR-0031 removed the HMAC option because a shared secret cannot be
        // published in a JWKS.
        Assert.Equal(
            "RS256",
            Assert.Single(document.RootElement.GetProperty("id_token_signing_alg_values_supported")
                .EnumerateArray()).GetString());

        Assert.Equal(
            "S256",
            Assert.Single(document.RootElement.GetProperty("code_challenge_methods_supported")
                .EnumerateArray()).GetString());
    }

    /// <summary>
    /// Every URL the document advertises answers — the rule, asserted rather than promised.
    /// </summary>
    /// <remarks>
    /// A client reading a discovery document is entitled to assume each URL in it exists. S8 lands in
    /// pieces, so the temptation is to advertise the whole surface and fill it in later; this test is what
    /// makes that impossible. When the authorization endpoint lands, it adds itself to the document **and**
    /// to the set this test walks, in the same unit.
    /// </remarks>
    [Fact]
    public async Task EveryAdvertisedUrl_AnswersAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using JsonDocument document = await ReadAsync(client, "/.well-known/openid-configuration");

        List<string> advertised = [.. document.RootElement.EnumerateObject()
            .Where(member => member.Name.EndsWith("_endpoint", StringComparison.Ordinal)
                || member.Name == "jwks_uri")
            .Select(member => member.Value.GetString()!)];

        // Act, Assert
        Assert.NotEmpty(advertised);

        foreach (string url in advertised)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(url), TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    /// <summary>A token this API signs verifies against the published key set, and nothing else.</summary>
    /// <remarks>
    /// The whole point of being a provider: something that has never spoken to this API can check what it
    /// said. The verification below is given the JWKS and the issuer and no other input.
    /// </remarks>
    [Fact]
    public async Task ATokenSignedByThisApi_VerifiesAgainstThePublishedKeysAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        ISigningKeyProvider keys = factory.Services.GetRequiredService<ISigningKeyProvider>();

        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://localhost",
            Audience = "a-client",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(keys.Current.Key, SecurityAlgorithms.RsaSha256),
        });

        string body = await client.GetStringAsync(
            new Uri("/.well-known/jwks.json", UriKind.Relative), TestContext.Current.CancellationToken);

        // Act — a verifier with the published set and nothing from inside this process.
        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                ValidIssuer = "https://localhost",
                ValidAudience = "a-client",
                IssuerSigningKeys = new JsonWebKeySet(body).GetSigningKeys(),
                ValidateLifetime = true,
            });

        // Assert
        Assert.True(result.IsValid, result.Exception?.Message);
    }

    /// <summary>The key set publishes no private material.</summary>
    /// <remarks>
    /// Asserted here as well as in `RsaSigningKeyProviderTests`, deliberately: that one proves the provider
    /// builds a clean set, and this one proves the route serves what the provider built. A serializer that
    /// reached for the underlying key would pass the first and fail this.
    /// </remarks>
    [Fact]
    public async Task ThePublishedKeySet_CarriesNoPrivateMaterialAsync()
    {
        // Arrange
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Act
        string body = await client.GetStringAsync(
            new Uri("/.well-known/jwks.json", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert — `d` is RSA's private exponent, `p` and `q` its primes.
        Assert.DoesNotContain("\"d\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"p\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"q\"", body, StringComparison.Ordinal);
        Assert.Contains("\"n\"", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            // A connection string that is never opened: these routes touch no database, and the host still
            // refuses to boot without one — ADR-0026.
            webHost.UseSetting("DATABASE_URL", "Host=localhost;Port=5432;Database=unused;Username=u;Password=p");
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);
            webHost.UseSetting(RsaSigningKeyProvider.CurrentKeyVariable, _pem);
        });

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path)
    {
        string body = await client.GetStringAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        return JsonDocument.Parse(body);
    }

    private static string NewKeyPem()
    {
        using RSA rsa = RSA.Create(2048);

        return rsa.ExportPkcs8PrivateKeyPem();
    }
}
