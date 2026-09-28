using MusicRoom.Application.Abstractions.Auditing;

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

    /// <summary>Takes the framework's accessor.</summary>
    /// <param name="httpContextAccessor">Where the current request is found.</param>
    public HttpClientContextAccessor(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public ClientContext? Current =>
        _httpContextAccessor.HttpContext?.Items[ClientContextMiddleware.ContextItemKey]
            is ClientContextValues values
            ? new ClientContext(
                values.Platform,
                values.Device,
                values.AppVersion,
                values.CorrelationId,
                values.UserId)
            : null;
}
