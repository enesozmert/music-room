using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicRoom.Application.Abstractions.Identity;

namespace MusicRoom.Infrastructure.Identity;

/// <summary>
/// The relying-party core: one implementation, every provider — ADR-0008, SPEC-041.
/// </summary>
/// <remarks>
/// Nothing in the validation path names a provider. A row of <see cref="SocialProviderOptions"/> says where
/// the discovery document is, which issuer and audience to expect, and whether there is a secret to exchange
/// a code with; everything after that is the same six checks in the same order.
/// <para>
/// **The checks fail closed and separately.** A wrong signature, a wrong `iss`, a wrong `aud`, an expired
/// token and an unverified address are five different refusals, and each is refused on its own rather than
/// because another happened to catch it. The handler is told only that the token was invalid: which check
/// failed is in the log, because a caller who learns which check failed learns how to pass it.
/// </para>
/// <para>
/// Discovery and the JWKS are cached per provider. The key set is re-fetched when a token names a key the
/// cache does not hold, which is what key rotation looks like from here — a provider rotates, a token arrives
/// signed with the new key, and the fetch happens once rather than on every request.
/// </para>
/// </remarks>
/// <param name="client">The HTTP client with the resilience pipeline its registration attached.</param>
/// <param name="providers">The configuration rows, by name.</param>
/// <param name="logger">Where a refused token's reason goes. Never the response.</param>
internal sealed class OpenIdConnectIdentityProvider(
    HttpClient client,
    IReadOnlyDictionary<string, SocialProviderOptions> providers,
    ILogger<OpenIdConnectIdentityProvider> logger) : ISocialIdentityProvider
{
    private readonly HttpClient _client = client;
    private readonly IReadOnlyDictionary<string, SocialProviderOptions> _providers = providers;
    private readonly ILogger<OpenIdConnectIdentityProvider> _logger = logger;
    private readonly Dictionary<string, ProviderMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _metadataLock = new(1, 1);

    /// <inheritdoc />
    public async Task<SocialAuthentication> AuthenticateAsync(
        SocialAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_providers.TryGetValue(request.Provider, out SocialProviderOptions? options))
        {
            return SocialAuthentication.Refused(SocialAuthenticationOutcome.UnknownProvider);
        }

        ProviderMetadata metadata = await MetadataAsync(options, cancellationToken).ConfigureAwait(false);

        string? idToken = await ExchangeAsync(options, metadata, request, cancellationToken)
            .ConfigureAwait(false);

        if (idToken is null)
        {
            return SocialAuthentication.Refused(SocialAuthenticationOutcome.ExchangeRefused);
        }

        return await ValidateAsync(options, metadata, idToken, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Exchanges the code, or answers null when the provider refuses it.</summary>
    /// <remarks>
    /// The secret is in the form body and never in a query string: a URL reaches logs, proxies and browser
    /// history, which is the same reason SPEC-016 kept the hub token out of one.
    /// </remarks>
    private async Task<string?> ExchangeAsync(
        SocialProviderOptions options,
        ProviderMetadata metadata,
        SocialAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        if (options.ClientSecret is null || metadata.TokenEndpoint is null)
        {
            _logger.LogWarning(
                "Provider {Provider} has no code exchange: secret present {HasSecret}, token endpoint present {HasEndpoint}",
                options.Name,
                options.ClientSecret is not null,
                metadata.TokenEndpoint is not null);

            return null;
        }

        using FormUrlEncodedContent form = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = request.Code,
            ["code_verifier"] = request.CodeVerifier,
            ["redirect_uri"] = request.RedirectUri,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
        });

        using HttpResponseMessage response = await _client
            .PostAsync(metadata.TokenEndpoint, form, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The provider's body is not logged: it echoes the code and sometimes the client id.
            _logger.LogWarning(
                "Provider {Provider} refused the code exchange with {StatusCode}",
                options.Name,
                (int)response.StatusCode);

            return null;
        }

        TokenResponse? body = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        return body?.IdToken;
    }

    /// <summary>The six checks, in order, each failing closed.</summary>
    private async Task<SocialAuthentication> ValidateAsync(
        SocialProviderOptions options,
        ProviderMetadata metadata,
        string idToken,
        CancellationToken cancellationToken)
    {
        JsonWebTokenHandler handler = new();

        TokenValidationParameters parameters = new()
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = await KeysAsync(options, metadata, cancellationToken).ConfigureAwait(false),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        TokenValidationResult result = await handler.ValidateTokenAsync(idToken, parameters)
            .ConfigureAwait(false);

        if (!result.IsValid)
        {
            _logger.LogWarning(
                result.Exception,
                "Provider {Provider} issued a token this API refused",
                options.Name);

            return SocialAuthentication.Refused(SocialAuthenticationOutcome.TokenInvalid);
        }

        string? subject = Claim(result, "sub");

        if (string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("Provider {Provider} issued a token with no subject", options.Name);

            return SocialAuthentication.Refused(SocialAuthenticationOutcome.TokenInvalid);
        }

        bool verified = string.Equals(Claim(result, "email_verified"), "true", StringComparison.OrdinalIgnoreCase);

        return SocialAuthentication.Of(new SocialIdentity(
            options.Name,
            subject,
            Claim(result, "email"),
            verified,
            Claim(result, "name")));
    }

    /// <summary>One claim, as a string, however the token spelled it.</summary>
    private static string? Claim(TokenValidationResult result, string name) =>
        result.Claims.TryGetValue(name, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    /// <summary>The provider's signing keys, fetched once and re-fetched when the set is empty.</summary>
    private async Task<IEnumerable<SecurityKey>> KeysAsync(
        SocialProviderOptions options,
        ProviderMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.Keys.Count > 0)
        {
            return metadata.Keys;
        }

        JsonWebKeySet set = await FetchKeysAsync(metadata.JwksUri, cancellationToken).ConfigureAwait(false);

        await _metadataLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _metadata[options.Name] = metadata with { Keys = [.. set.GetSigningKeys()] };
        }
        finally
        {
            _metadataLock.Release();
        }

        return set.GetSigningKeys();
    }

    private async Task<JsonWebKeySet> FetchKeysAsync(Uri jwksUri, CancellationToken cancellationToken)
    {
        string body = await _client.GetStringAsync(jwksUri, cancellationToken).ConfigureAwait(false);

        return new JsonWebKeySet(body);
    }

    /// <summary>The discovery document, fetched once per provider.</summary>
    private async Task<ProviderMetadata> MetadataAsync(
        SocialProviderOptions options,
        CancellationToken cancellationToken)
    {
        await _metadataLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_metadata.TryGetValue(options.Name, out ProviderMetadata? cached))
            {
                return cached;
            }
        }
        finally
        {
            _metadataLock.Release();
        }

        DiscoveryDocument document = await _client
            .GetFromJsonAsync<DiscoveryDocument>(options.DiscoveryUrl, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The discovery document at {options.DiscoveryUrl} was empty.");

        ProviderMetadata metadata = new(
            document.TokenEndpoint is null ? null : new Uri(document.TokenEndpoint),
            new Uri(document.JwksUri),
            []);

        await _metadataLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _metadata[options.Name] = metadata;
        }
        finally
        {
            _metadataLock.Release();
        }

        return metadata;
    }

    /// <summary>What this API reads out of a discovery document, and nothing more.</summary>
    /// <param name="TokenEndpoint">Where a code is exchanged. **Absent at Facebook** — ADR-0008 measured that.</param>
    /// <param name="JwksUri">Where the signing keys are published.</param>
    private sealed record DiscoveryDocument(
        [property: System.Text.Json.Serialization.JsonPropertyName("token_endpoint")] string? TokenEndpoint,
        [property: System.Text.Json.Serialization.JsonPropertyName("jwks_uri")] string JwksUri);

    /// <summary>The token endpoint's answer. Only the ID token is read: identity is never an access token.</summary>
    /// <param name="IdToken">The signed assertion of who authenticated.</param>
    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("id_token")] string? IdToken);

    /// <summary>What the core caches per provider.</summary>
    /// <param name="TokenEndpoint">From discovery, or null where the provider publishes none.</param>
    /// <param name="JwksUri">From discovery.</param>
    /// <param name="Keys">Fetched on first use and replaced when a rotation empties them.</param>
    private sealed record ProviderMetadata(
        Uri? TokenEndpoint,
        Uri JwksUri,
        IReadOnlyCollection<SecurityKey> Keys);
}
