using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MusicRoom.Infrastructure.Configuration;

/// <summary>
/// Talks to the self-hosted Infisical SPEC-042 put in this compose stack — ADR-0029, SPEC-047.
/// </summary>
/// <remarks>
/// Two calls and nothing else: a Universal Auth login with the machine identity, then one fetch of the
/// project's secrets for one environment. Both happen once, at start-up, and `InfisicalSecretProvider` holds
/// what comes back.
/// <para>
/// **Its own class because it is its own responsibility.** The provider decides what a read means; this
/// decides how to ask a vault. The split is also what lets the provider's tests run with no network at all.
/// </para>
/// <para>
/// **Public, like this project's other adapters**, because the composition root builds one before a
/// container exists and because its refusals are what SPEC-047's tests assert.
/// </para>
/// <para>
/// **Nothing here logs, and no message carries a value.** A failure names the endpoint and the variable to
/// check, which is everything an operator needs and nothing an attacker does — `secrets-management`.
/// </para>
/// </remarks>
public sealed class InfisicalVaultClient
{
    /// <summary>Where a machine identity exchanges its credentials for a token.</summary>
    public const string LoginPath = "api/v1/auth/universal-auth/login";

    /// <summary>Where a project's secrets are read, in their raw (decrypted) form.</summary>
    public const string SecretsPath = "api/v3/secrets/raw";

    private readonly HttpClient _client;
    private readonly InfisicalOptions _options;

    /// <summary>Takes the transport and the four values that name a vault and an identity.</summary>
    /// <param name="client">The HTTP client. Its base address is this vault's site URL.</param>
    /// <param name="options">Where the vault is, who we are, and which project and environment to read.</param>
    public InfisicalVaultClient(HttpClient client, InfisicalOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _options = options;
    }

    /// <summary>
    /// Logs in and reads every secret of the configured project and environment.
    /// </summary>
    /// <param name="cancellationToken">The start-up cancellation token.</param>
    /// <returns>The secrets, by name. Ordinal keys: a secret name is a name, not a word.</returns>
    /// <exception cref="InvalidOperationException">
    /// The vault refused, could not be reached, or answered something that is not a secret list. Every
    /// message names what to check and carries no value.
    /// </exception>
    public async Task<IReadOnlyDictionary<string, string>> FetchAsync(CancellationToken cancellationToken)
    {
        string token = await LoginAsync(cancellationToken).ConfigureAwait(false);

        Uri path = new(
            $"{SecretsPath}?workspaceId={Uri.EscapeDataString(_options.ProjectId)}"
            + $"&environment={Uri.EscapeDataString(_options.Environment)}&secretPath=%2F",
            UriKind.Relative);

        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw Refused(
                $"the vault answered {(int)response.StatusCode}"
                + $"{await ReasonAsync(response, cancellationToken).ConfigureAwait(false)} when asked for "
                + "the secrets of project INFISICAL_PROJECT_ID in environment INFISICAL_ENVIRONMENT; the "
                + "machine identity also has to be a member of that project");
        }

        SecretsResponse? body = await response.Content
            .ReadFromJsonAsync<SecretsResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (body?.Secrets is null)
        {
            throw Refused("the vault's answer carried no secret list");
        }

        Dictionary<string, string> secrets = new(StringComparer.Ordinal);

        // Last one wins, which only matters if a vault returned a name twice. Not an error worth stopping a
        // host for, and not something this class can resolve on the operator's behalf.
        foreach (SecretEntry entry in body.Secrets.Where(entry => !string.IsNullOrEmpty(entry.Key)))
        {
            secrets[entry.Key!] = entry.Value ?? string.Empty;
        }

        return secrets;
    }

    /// <summary>Exchanges the machine identity's credentials for a short-lived access token.</summary>
    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(LoginPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginRequest(_options.ClientId, _options.ClientSecret)),
        };

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The status, the vault's own reason, and the names of the two variables to check. Never the
            // credentials. The vault's reason is worth carrying because it is the difference between "that
            // identity does not exist here" and "that secret is wrong", which an operator cannot otherwise
            // tell apart — and it was the first thing a live run needed (SPEC-047).
            throw Refused(
                $"the vault refused the machine identity with {(int)response.StatusCode}"
                + $"{await ReasonAsync(response, cancellationToken).ConfigureAwait(false)}; check "
                + "INFISICAL_CLIENT_ID and INFISICAL_CLIENT_SECRET, and that the identity exists in this "
                + "vault");
        }

        LoginResponse? body = await response.Content
            .ReadFromJsonAsync<LoginResponse>(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrEmpty(body?.AccessToken)
            ? throw Refused("the vault's login answer carried no access token")
            : body.AccessToken;
    }

    /// <summary>Sends, turning a transport failure into the same refusal shape as a rejection.</summary>
    /// <remarks>
    /// A vault that is not running and a vault that says no are one answer to a host that cannot start
    /// either way, and both messages name <c>SECRET_PROVIDER</c> so the operator knows which switch put them
    /// here.
    /// </remarks>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw Refused(
                "the vault could not be reached at INFISICAL_SITE_URL; `make vault` starts it",
                exception);
        }
        catch (TaskCanceledException exception)
        {
            throw Refused("the vault did not answer in time at INFISICAL_SITE_URL", exception);
        }
    }

    /// <summary>
    /// The vault's own explanation, as ` ("...")`, or nothing when it offered none.
    /// </summary>
    /// <remarks>
    /// Bounded and taken from one member, deliberately. The whole body could be large and could in principle
    /// echo something that was sent; `message` on an Infisical error is a sentence about the request's shape
    /// — *"No identity with specified client ID was found"* — which is the one thing an operator needs and
    /// is not a secret. Nothing else from the body is read.
    /// </remarks>
    private static async Task<string> ReasonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const int longestReason = 200;

        try
        {
            VaultError? error = await response.Content
                .ReadFromJsonAsync<VaultError>(cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(error?.Message)
                ? string.Empty
                : $" (\"{error.Message[..Math.Min(error.Message.Length, longestReason)]}\")";
        }
        catch (System.Text.Json.JsonException)
        {
            // A vault that answered something other than its own error shape explains nothing, and a
            // failure to read a failure must not replace it.
            return string.Empty;
        }
    }

    private static InvalidOperationException Refused(string detail, Exception? inner = null) =>
        new(
            $"SECRET_PROVIDER is `infisical`, so this application reads its configuration from the vault, and "
            + $"{detail}. Nothing falls back to .env on purpose: a vault outage must not quietly return this "
            + "system to reading secrets out of a file — ADR-0029, SPEC-047.",
            inner);

    /// <summary>What a Universal Auth login sends.</summary>
    private sealed record LoginRequest(
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("clientSecret")] string ClientSecret);

    /// <summary>What it answers. Only the token is read; the lifetime is not, because nothing refreshes.</summary>
    private sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken);

    /// <summary>The raw-secrets answer.</summary>
    private sealed record SecretsResponse(
        [property: JsonPropertyName("secrets")] IReadOnlyList<SecretEntry>? Secrets);

    /// <summary>The one member of an error body this client reads.</summary>
    private sealed record VaultError(
        [property: JsonPropertyName("message")] string? Message);

    /// <summary>One secret, as Infisical's raw endpoint names its parts.</summary>
    private sealed record SecretEntry(
        [property: JsonPropertyName("secretKey")] string? Key,
        [property: JsonPropertyName("secretValue")] string? Value);
}

/// <summary>
/// Where the vault is, who we are to it, and what to read — the bootstrap values that stay in `.env`.
/// </summary>
/// <param name="SiteUrl">The vault's base address, from <c>INFISICAL_SITE_URL</c>.</param>
/// <param name="ClientId">The machine identity, from <c>INFISICAL_CLIENT_ID</c>.</param>
/// <param name="ClientSecret">Its secret, from <c>INFISICAL_CLIENT_SECRET</c>.</param>
/// <param name="ProjectId">The project to read, from <c>INFISICAL_PROJECT_ID</c>.</param>
/// <param name="Environment">Which environment of it, from <c>INFISICAL_ENVIRONMENT</c>.</param>
/// <remarks>
/// These five cannot come from the vault, which is the sentence ADR-0029 wrote before any of this existed:
/// *"a vault does not remove the local secret file; it shrinks it."*
/// </remarks>
public sealed record InfisicalOptions(
    Uri SiteUrl,
    string ClientId,
    string ClientSecret,
    string ProjectId,
    string Environment);
