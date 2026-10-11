using System.Net;
using System.Text;

namespace MusicRoom.Infrastructure.Tests.Configuration;

/// <summary>
/// A vault that answers from memory — SPEC-047.
/// </summary>
/// <remarks>
/// No test reaches Infisical. A suite that did would fail whenever the network or a container did, and it
/// could not produce the answers these tests exist to refuse: rejected credentials, a login with no token,
/// and a vault that is simply not there.
/// </remarks>
internal sealed class VaultTransport : HttpMessageHandler
{
    private readonly string _loginBody;
    private readonly HttpStatusCode _loginStatus;
    private readonly string _secretsBody;
    private readonly HttpStatusCode _secretsStatus;

    /// <summary>Takes what each of the two endpoints will answer.</summary>
    /// <param name="loginBody">The login endpoint's body.</param>
    /// <param name="secretsBody">The raw-secrets endpoint's body.</param>
    /// <param name="loginStatus">The login endpoint's status.</param>
    /// <param name="secretsStatus">The raw-secrets endpoint's status.</param>
    internal VaultTransport(
        string loginBody,
        string secretsBody,
        HttpStatusCode loginStatus = HttpStatusCode.OK,
        HttpStatusCode secretsStatus = HttpStatusCode.OK)
    {
        _loginBody = loginBody;
        _secretsBody = secretsBody;
        _loginStatus = loginStatus;
        _secretsStatus = secretsStatus;
    }

    /// <summary>Every request this transport was given, in order.</summary>
    internal List<Uri> Requests { get; } = [];

    /// <summary>The bearer token the secrets request carried, or null if it carried none.</summary>
    internal string? PresentedToken { get; private set; }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri uri = request.RequestUri!;
        Requests.Add(uri);

        if (uri.AbsolutePath.Contains("universal-auth/login", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(_loginStatus, _loginBody));
        }

        PresentedToken = request.Headers.Authorization?.Parameter;

        return Task.FromResult(Json(_secretsStatus, _secretsBody));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}

/// <summary>A transport that fails the way an absent vault fails.</summary>
/// <remarks>
/// `HttpRequestException` is what a connection refused looks like from inside `HttpClient`, and the refusal
/// this proves — a host that stops rather than falling back to `.env` — is the whole security property of
/// SPEC-047.
/// </remarks>
internal sealed class UnreachableVaultTransport : HttpMessageHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("connection refused");
}

/// <summary>
/// A vault that refuses a few times and then answers — a container finishing its start-up, SPEC-051.
/// </summary>
/// <remarks>
/// What a free-tier platform does to a dependency: the first requests reach a service that is still
/// waking, and only then does it serve. The bounded retry in `SecretProviderFactory` exists for exactly
/// this shape, and a transport that is only ever up or only ever down cannot express it.
/// </remarks>
internal sealed class FlakyVaultTransport : HttpMessageHandler
{
    private readonly string _loginBody;
    private readonly string _secrets;

    private int _remainingRefusals;

    /// <summary>Takes how many times to refuse, then what to answer.</summary>
    /// <param name="refusals">How many requests fail before the vault is up.</param>
    /// <param name="loginBody">The login endpoint's body once it is.</param>
    /// <param name="secrets">The raw-secrets endpoint's body once it is.</param>
    internal FlakyVaultTransport(int refusals, string loginBody, string secrets)
    {
        _remainingRefusals = refusals;
        _loginBody = loginBody;
        _secrets = secrets;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_remainingRefusals > 0)
        {
            _remainingRefusals--;

            throw new HttpRequestException("connection refused");
        }

        bool login = request.RequestUri!.AbsolutePath
            .Contains("universal-auth/login", StringComparison.Ordinal);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(login ? _loginBody : _secrets, Encoding.UTF8, "application/json"),
        });
    }
}
