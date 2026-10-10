using System.Net;
using System.Text;

namespace MusicRoom.Infrastructure.Tests.Identity;

/// <summary>
/// A provider that answers from memory: a discovery document, a key set and a token endpoint — SPEC-041.
/// </summary>
/// <remarks>
/// No test reaches Google. A suite that did would fail whenever the network did, and it could not forge the
/// tokens these tests exist to refuse: a wrong issuer, a wrong audience and an expired token are things a real
/// provider will never send.
/// </remarks>
internal sealed class ProviderTransport : HttpMessageHandler
{
    private readonly string _discovery;
    private readonly string _keys;
    private readonly string _tokenBody;
    private readonly HttpStatusCode _tokenStatus;

    /// <summary>Takes what each of the three endpoints will answer.</summary>
    /// <param name="discovery">The discovery document body.</param>
    /// <param name="keys">The JWKS body.</param>
    /// <param name="tokenBody">The token endpoint's body.</param>
    /// <param name="tokenStatus">The token endpoint's status.</param>
    internal ProviderTransport(
        string discovery,
        string keys,
        string tokenBody,
        HttpStatusCode tokenStatus = HttpStatusCode.OK)
    {
        _discovery = discovery;
        _keys = keys;
        _tokenBody = tokenBody;
        _tokenStatus = tokenStatus;
    }

    /// <summary>Every request this transport was given, in order.</summary>
    internal List<Uri> Requests { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri uri = request.RequestUri!;
        Requests.Add(uri);

        if (uri.AbsolutePath.Contains("openid-configuration", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(HttpStatusCode.OK, _discovery));
        }

        if (uri.AbsolutePath.Contains("jwks", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(HttpStatusCode.OK, _keys));
        }

        return Task.FromResult(Json(_tokenStatus, _tokenBody));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
