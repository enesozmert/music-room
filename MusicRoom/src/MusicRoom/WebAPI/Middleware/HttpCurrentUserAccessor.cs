using System.Security.Claims;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.WebAPI.Middleware;

/// <summary>
/// Reads the caller off the validated principal.
/// </summary>
/// <remarks>
/// Nothing here parses a token: by the time this runs, <c>UseAuthentication</c> has verified the signature,
/// the issuer, the audience and the lifetime, and a claim that is present is a claim this application signed.
/// </remarks>
internal sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Takes the framework's accessor.</summary>
    /// <param name="httpContextAccessor">Where the current request is found.</param>
    public HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public Guid? UserId => Claim(ClaimTypes.NameIdentifier, "sub");

    /// <inheritdoc />
    public Guid? SessionId => Claim(ClaimTypes.Sid, "sid");

    private Guid? Claim(string mappedType, string rawType)
    {
        ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;

        string? value = principal?.FindFirst(mappedType)?.Value ?? principal?.FindFirst(rawType)?.Value;

        return Guid.TryParse(value, out Guid parsed) ? parsed : null;
    }
}
