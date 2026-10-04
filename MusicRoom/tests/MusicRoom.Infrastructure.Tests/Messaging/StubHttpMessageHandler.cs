using System.Net;

namespace MusicRoom.Infrastructure.Tests.Messaging;

/// <summary>
/// A handler that answers from memory and keeps what it was asked — SPEC-024 decision 7.
/// </summary>
/// <remarks>
/// No test sends a real message: a test that called Brevo would spend quota on every CI run (T-19) and fail
/// whenever the network did. This handler is what makes the sender's behaviour assertable without either.
/// </remarks>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    /// <summary>Takes the answer every request will receive.</summary>
    /// <param name="status">The status code to answer with.</param>
    /// <param name="body">The body to answer with.</param>
    internal StubHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    /// <summary>The requests this handler was given, in order, with their bodies already read.</summary>
    internal List<CapturedRequest> Requests { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.TryGetValues("api-key", out IEnumerable<string>? keys) ? keys.First() : null,
            body));

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// One request, as the handler saw it.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The absolute request URI, base address included.</param>
/// <param name="ApiKey">The value of the `api-key` header, or null when it was absent.</param>
/// <param name="Body">The serialised request body.</param>
internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? ApiKey, string Body);
