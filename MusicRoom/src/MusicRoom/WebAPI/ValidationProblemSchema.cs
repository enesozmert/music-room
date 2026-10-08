using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MusicRoom.WebAPI;

/// <summary>
/// Declares the `codes` and `code` members the error bodies carry — ADR-0045.
/// </summary>
/// <remarks>
/// RFC 9457 allows extension members, and the framework's `HttpValidationProblemDetails` schema does not know
/// about the ones this API adds. An undeclared member is one no generated client can see, which is the same
/// defect as not sending it: a mobile client generating its models from `docs/api/openapi.json` would have
/// `errors` and nothing to switch on.
/// <para>
/// A schema transformer rather than a hand-edited document, because `make agent-check` regenerates the
/// document from the code and fails on any difference. A member written into the file by hand would be erased
/// by the next generation, and a reviewer would see a drift failure rather than a decision.
/// </para>
/// <para>
/// Its own file for the reason `InteractiveDocumentation` has one: `Program.cs` is at S104's 300-line cap,
/// which is issue O24.
/// </para>
/// </remarks>
internal static class ValidationProblemSchema
{
    /// <summary>The member a validation failure carries beside `errors`.</summary>
    internal const string CodesMember = "codes";

    /// <summary>The member a handler refusal carries, which has no field to key on.</summary>
    internal const string CodeMember = "code";

    /// <summary>Adds the transformer that declares both members.</summary>
    /// <param name="options">The document options being configured.</param>
    internal static void Declare(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSchemaTransformer((schema, context, _) =>
        {
            string? name = context.JsonTypeInfo.Type.Name;

            if (name is "HttpValidationProblemDetails")
            {
                (schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal))
                    [CodesMember] = CodesSchema();
            }
            else if (name is "ProblemDetails")
            {
                (schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal))
                    [CodeMember] = CodeSchema();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>`codes`: the same keys as `errors`, each carrying the codes for that field.</summary>
    private static OpenApiSchema CodesSchema() =>
        new()
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            Description =
                "The stable code for each message in `errors`, under the same keys and in the same order — "
                + "ADR-0045. A client switches on these and renders `errors`.",
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };

    /// <summary>`code`: the single code a refusal with no field carries.</summary>
    private static OpenApiSchema CodeSchema() =>
        new()
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description =
                "The stable code for a refusal that names no field, such as a wrong current password — "
                + "ADR-0045.",
        };
}
