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
    private static readonly Lazy<JsonDocument> _document = new(LoadDocument);

    private static readonly System.Text.RegularExpressions.Regex _indexerPattern =
        new(@"\[\d+\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static JsonDocument Document => _document.Value;

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

    /// <summary>
    /// Asserts that every key is a field the route's published request schema carries — ADR-0043, SPEC-037.
    /// </summary>
    /// <param name="routeTemplate">The path exactly as `docs/api/openapi.json` spells it.</param>
    /// <param name="method">The HTTP method, lower case.</param>
    /// <param name="keys">The keys read out of a `422`.</param>
    /// <remarks>
    /// The point of asserting against the document rather than against a list of expected strings: a route added
    /// tomorrow with a PascalCase key fails this without anybody remembering to extend a list. An indexer is
    /// stripped before the comparison, because `genres[0]` is the second entry of a field called `genres`.
    /// </remarks>
    internal static void AssertKeysAreRequestFields(string routeTemplate, string method, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        IReadOnlyCollection<string> fields = RequestFields(routeTemplate, method);

        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            string field = _indexerPattern.Replace(key, string.Empty);

            Assert.Contains(field, fields);
        }
    }

    /// <summary>
    /// Every field name a route accepts: the request body's properties, dotted for a nested object, plus its
    /// query parameters.
    /// </summary>
    /// <param name="routeTemplate">The path exactly as the document spells it.</param>
    /// <param name="method">The HTTP method, lower case.</param>
    /// <returns>The field paths, as a client would write them.</returns>
    internal static IReadOnlyCollection<string> RequestFields(string routeTemplate, string method)
    {
        JsonElement operation = Document.RootElement
            .GetProperty("paths")
            .GetProperty(routeTemplate)
            .GetProperty(method);

        HashSet<string> fields = new(StringComparer.Ordinal);

        if (operation.TryGetProperty("parameters", out JsonElement parameters))
        {
            foreach (JsonElement parameter in parameters.EnumerateArray())
            {
                if (parameter.GetProperty("in").GetString() == "query")
                {
                    fields.Add(parameter.GetProperty("name").GetString()!);
                }
            }
        }

        if (operation.TryGetProperty("requestBody", out JsonElement body))
        {
            Collect(
                Resolve(body.GetProperty("content").GetProperty("application/json").GetProperty("schema")),
                prefix: string.Empty,
                fields);
        }

        return fields;
    }

    private static void Collect(JsonElement schema, string prefix, HashSet<string> fields)
    {
        if (!schema.TryGetProperty("properties", out JsonElement properties))
        {
            return;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            fields.Add(path);

            // One level of nesting is what the API has: a profile field carries its value and its tier, and a
            // geofence carries three numbers. A deeper body would need this to recurse further, and the test
            // asserting it would say so.
            Collect(Resolve(property.Value), path, fields);
        }
    }

    /// <summary>
    /// Follows a `$ref`, and the `oneOf: [null, ...]` wrapper the generator emits for a nullable body or field.
    /// </summary>
    private static JsonElement Resolve(JsonElement schema)
    {
        while (true)
        {
            if (schema.TryGetProperty("$ref", out JsonElement reference))
            {
                string name = reference.GetString()!.Split('/')[^1];
                schema = Document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
                continue;
            }

            if (!schema.TryGetProperty("oneOf", out JsonElement alternatives))
            {
                return schema;
            }

            JsonElement? concrete = Concrete(alternatives);

            if (concrete is null)
            {
                return schema;
            }

            schema = concrete.Value;
        }
    }

    /// <summary>The alternative of a `oneOf` that is not the null one.</summary>
    private static JsonElement? Concrete(JsonElement alternatives)
    {
        foreach (JsonElement alternative in alternatives.EnumerateArray())
        {
            if (alternative.TryGetProperty("$ref", out _) || alternative.TryGetProperty("properties", out _))
            {
                return alternative;
            }
        }

        return null;
    }

    private static JsonDocument LoadDocument()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "docs", "api", "openapi.json");

            if (File.Exists(candidate))
            {
                return JsonDocument.Parse(File.ReadAllText(candidate));
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "docs/api/openapi.json was not found above " + AppContext.BaseDirectory
            + ". It is the published contract this test asserts keys against — SPEC-037.");
    }
}
