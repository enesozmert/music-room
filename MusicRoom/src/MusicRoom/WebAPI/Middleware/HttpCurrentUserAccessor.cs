using System.Security.Claims;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.WebAPI.Hubs;

namespace MusicRoom.WebAPI.Middleware;

/// <summary>
/// Reads the caller off the validated principal.
/// </summary>
/// <remarks>
/// Nothing here parses a token: by the time this runs, <c>UseAuthentication</c> has verified the signature,
/// the issuer, the audience and the lifetime, and a claim that is present is a claim this application signed.
/// <para>
/// A hub invocation has no <c>HttpContext</c> — the handshake had one, the invocation does not — so this
/// falls back to the per-invocation holder the hub filter fills. One accessor for both transports is the
/// point: a second one would let the same handler be authorised differently depending on how it was reached,
/// which is what `signalr-realtime` forbids — SPEC-016.
/// </para>
/// </remarks>
internal sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HubCaller _hubCaller;

    /// <summary>Takes the framework's accessor and the hub's per-invocation holder.</summary>
    /// <param name="httpContextAccessor">Where the current request is found.</param>
    /// <param name="hubCaller">Who is calling when the transport is a hub — SPEC-016.</param>
    public HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor, HubCaller hubCaller)
    {
        _httpContextAccessor = httpContextAccessor;
        _hubCaller = hubCaller;
    }

    /// <inheritdoc />
    public Guid? UserId => Claim(ClaimTypes.NameIdentifier, "sub") ?? _hubCaller.UserId;

    /// <inheritdoc />
    public Guid? SessionId => Claim(ClaimTypes.Sid, "sid") ?? _hubCaller.SessionId;

    private Guid? Claim(string mappedType, string rawType)
    {
        ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;

        string? value = principal?.FindFirst(mappedType)?.Value ?? principal?.FindFirst(rawType)?.Value;

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }
}
