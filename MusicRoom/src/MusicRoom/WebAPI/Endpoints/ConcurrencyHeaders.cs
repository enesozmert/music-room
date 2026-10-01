using System.Globalization;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The `ETag` and `If-Match` of an aggregate guarded by PostgreSQL's <c>xmin</c> — SPEC-014, SDD §12.2.
/// </summary>
/// <remarks>
/// Shared by every aggregate that carries a concurrency token: playlists, their track lists, and events. One
/// implementation, because two spellings of "read the `If-Match` header" is how one route ends up accepting a
/// wildcard the other refuses.
/// </remarks>
internal static class ConcurrencyHeaders
{
    /// <summary>The `xmin` of a row, as a quoted strong `ETag`.</summary>
    /// <param name="version">The row version.</param>
    /// <returns>The header value.</returns>
    internal static string ETag(uint version) =>
        $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// Reads `If-Match` back into an `xmin`. A missing, malformed or wildcard header fails: a wildcard means
    /// "whatever is there", which is exactly the last-write-wins behaviour this guard exists to prevent.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="expectedVersion">The version the caller claims to hold.</param>
    /// <returns>True when the header carried a usable version.</returns>
    internal static bool TryReadIfMatch(HttpContext httpContext, out uint expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        expectedVersion = 0;

        string? header = httpContext.Request.Headers.IfMatch.ToString();

        return !string.IsNullOrWhiteSpace(header)
            && uint.TryParse(
                header.Trim().Trim('"'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out expectedVersion);
    }
}
