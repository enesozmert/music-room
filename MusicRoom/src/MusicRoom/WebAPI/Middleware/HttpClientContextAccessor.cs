using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Middleware;

/// <summary>
/// Supplies the current request's client context to the application pipeline.
/// </summary>
/// <remarks>
/// The one implementation of an <c>Application</c> abstraction that lives in <c>WebAPI</c>, because
/// <c>HttpContext</c> is an ASP.NET concept: putting it in <c>Infrastructure</c> would make that project
/// reference the web stack to read three strings. The composition root supplies it, so no inward reference
/// is broken — ADR-0028, and the SPEC-008 Files table walk.
/// </remarks>
internal sealed class HttpClientContextAccessor : IClientContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HubCaller _hubCaller;

    /// <summary>Takes the framework's accessor and the hub's per-invocation holder.</summary>
    /// <param name="httpContextAccessor">Where the current request is found.</param>
    /// <param name="hubCaller">The context captured at a hub handshake — SPEC-016, ADR-0019.</param>
    public HttpClientContextAccessor(IHttpContextAccessor httpContextAccessor, HubCaller hubCaller)
    {
        _httpContextAccessor = httpContextAccessor;
        _hubCaller = hubCaller;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The HTTP request first, the hub invocation second. A hub method carries no headers of its own, so its
    /// context comes from the handshake by way of the filter — and a handshake without one was refused before
    /// any method could run.
    /// </remarks>
    public ClientContext? Current =>
        _httpContextAccessor.HttpContext?.Items[ClientContextMiddleware.ContextItemKey]
            is ClientContextValues values
            ? new ClientContext(
                values.Platform,
                values.Device,
                values.AppVersion,
                values.CorrelationId,
                values.UserId,
                values.Language)
            : _hubCaller.Context;
}
