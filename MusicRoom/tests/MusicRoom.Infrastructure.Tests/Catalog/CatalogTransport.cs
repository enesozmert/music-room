using System.Net;

namespace MusicRoom.Infrastructure.Tests.Catalog;

/// <summary>
/// A transport that answers from a queue and keeps every request URI — SPEC-025 decision 7.
/// </summary>
/// <remarks>
/// No test calls YouTube. A test that did would spend the project's daily quota on every CI run (T-10) and fail
/// whenever the network did, which is the opposite of evidence.
/// </remarks>
internal sealed class CatalogTransport : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();

    /// <summary>Takes the answers, in the order they will be given.</summary>
    /// <param name="answers">One entry per expected call.</param>
    internal CatalogTransport(params (HttpStatusCode Status, string Body)[] answers)
    {
        foreach ((HttpStatusCode status, string body) in answers)
        {
            _answers.Enqueue((status, body));
        }
    }

    /// <summary>Every request URI this transport was given, in order.</summary>
    internal List<string> Requests { get; } = [];

    /// <summary>How many calls reached the provider.</summary>
    internal int CallCount => Requests.Count;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Requests.Add(request.RequestUri?.ToString() ?? string.Empty);

        (HttpStatusCode status, string body) = _answers.Count > 0
            ? _answers.Dequeue()

            // An unexpected call is answered `500` rather than throwing, so a test that over-calls fails on the
            // assertion it is about instead of on a transport error.
            : (HttpStatusCode.InternalServerError, "{}");

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
