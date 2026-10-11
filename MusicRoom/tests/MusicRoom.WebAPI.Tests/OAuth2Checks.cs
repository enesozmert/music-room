using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// What the provider's answers have to look like — the assertions <see cref="OAuth2Driver"/>'s suites share.
/// </summary>
/// <remarks>
/// Split from the driver when SPEC-045 added UserInfo and revocation and pushed that file past the 300 lines
/// the root .editorconfig allows (S104). The seam is the obvious one: the driver sends requests, this reads
/// answers.
/// </remarks>
internal static class OAuth2Checks
{
    /// <summary>The query parameters a redirect carried back.</summary>
    /// <param name="response">The `302`.</param>
    /// <returns>The parsed query.</returns>
    internal static Dictionary<string, StringValues> Returned(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location!;

        return QueryHelpers.ParseQuery(location.Query);
    }

    /// <summary>Asserts an RFC 6749 §5.2 error object.</summary>
    /// <param name="response">The refusal.</param>
    /// <param name="status">The status expected.</param>
    /// <param name="error">The specification's error code expected.</param>
    /// <returns>A task that completes when the body has been read.</returns>
    internal static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string error)
    {
        Assert.Equal(status, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(error, body.RootElement.GetProperty("error").GetString());
    }

    /// <summary>Asserts ADR-0045's `code` member on a ProblemDetails refusal.</summary>
    /// <param name="response">The refusal.</param>
    /// <param name="code">The code expected.</param>
    /// <returns>A task that completes when the body has been read.</returns>
    internal static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A verifier of the length RFC 7636 asks for, from the generator a client would use.</summary>
    /// <returns>The verifier.</returns>
    internal static string Verifier() =>
        System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
