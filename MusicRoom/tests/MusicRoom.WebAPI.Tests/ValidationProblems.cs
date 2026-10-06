using System.Text.Json;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Reads the `errors` dictionary a `422` carries — SPEC-036, issue O26.
/// </summary>
/// <remarks>
/// Its own file, like `EventRequests`, `DeviceRequests`, `PlaybackRequests` and `PlaylistTrackRequests`: a helper
/// inlined into fourteen suites is fourteen copies, and three of those suites are within ten lines of the
/// 300-line limit the root .editorconfig enforces.
/// <para>
/// **What this exists to catch.** `CredentialEndpointResults.ToErrors` groups validation failures by the property
/// name FluentValidation reported against, so the body is <c>{"errors":{"PropertyName":["message"]}}</c> and those
/// names are a published contract — `HttpValidationProblemDetails` appears on 22 operations in the committed
/// `docs/api/openapi.json`. The OpenAPI drift guard cannot help: the schema describes the envelope, not the keys
/// inside it. Before SPEC-034 nothing asserted a key, so renaming a validated property changed a response shape
/// clients read and no test failed. This is what fails now.
/// </para>
/// </remarks>
internal static class ValidationProblems
{
    /// <summary>The property names the response keys its `errors` dictionary on.</summary>
    /// <param name="response">The `422` being read. Not disposed here — the caller owns it.</param>
    /// <returns>The keys, in the order the body lists them.</returns>
    internal static async Task<string[]> KeysAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using JsonDocument document = await ReadAsync(response);

        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }

    /// <summary>The messages the response reported against one key.</summary>
    /// <param name="response">The `422` being read.</param>
    /// <param name="key">The property name, exactly as the body spells it.</param>
    /// <returns>Every message under that key.</returns>
    /// <remarks>
    /// Separate from <see cref="KeysAsync"/> because the two assert different things: a key is the contract a
    /// client reads, and a message is the sentence a person reads. A test that wants both says so twice.
    /// </remarks>
    internal static async Task<string[]> MessagesAsync(HttpResponseMessage response, string key)
    {
        ArgumentNullException.ThrowIfNull(response);

        using JsonDocument document = await ReadAsync(response);

        return
        [
            .. document.RootElement
                .GetProperty("errors")
                .GetProperty(key)
                .EnumerateArray()
                .Select(message => message.GetString() ?? string.Empty)
        ];
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        using Stream body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
    }
}
