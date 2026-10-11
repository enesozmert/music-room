using MusicRoom.Infrastructure.Security;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// What a standard client reads before it talks to this API — ADR-0031, SPEC-043.
/// </summary>
/// <remarks>
/// Two routes, both anonymous and both cacheable: a discovery document saying what this provider offers, and
/// the public key set that makes a token it issued verifiable by something that has never spoken to it. That
/// second sentence is why ADR-0031 removed the HMAC option — *"a shared secret cannot be published in a JWKS,
/// and a JWKS is what makes the provider verifiable by anything other than itself."*
/// <para>
/// **The document advertises only what answers.** `authorization_endpoint` and `token_endpoint` joined it in
/// SPEC-044, in the same unit that made them answer; `userinfo_endpoint` is still absent rather than present
/// and broken, because a client reading a discovery document is entitled to assume every URL in it exists and
/// `OpenIdMetadataEndpointTests` asserts exactly that by calling each one.
/// </para>
/// </remarks>
internal static class OpenIdMetadataEndpoints
{
    /// <summary>Where the discovery document lives — the path the specification fixes.</summary>
    internal const string DiscoveryPath = "/.well-known/openid-configuration";

    /// <summary>Where the public key set lives.</summary>
    internal const string JwksPath = "/.well-known/jwks.json";

    /// <summary>Maps both routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapOpenIdMetadata(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Resolved here, at map time, so a missing or unusable key stops the host — ADR-0031's posture and
        // the same shape as the mail sender's guard. Left to the first request, a host with no signing key
        // would look healthy until somebody tried to verify a token, which is the worst moment to find out.
        _ = endpoints.ServiceProvider.GetRequiredService<ISigningKeyProvider>();

        endpoints.MapAuthorizeEndpoint();
        endpoints.MapTokenEndpoint();

        endpoints.MapGet(DiscoveryPath, Discovery)
            .AllowAnonymous()
            .WithName("GetOpenIdConfiguration")
            .WithSummary("OpenID Connect discovery document")
            .WithDescription(
                "What this provider offers. Only endpoints that answer are listed: a client reading this "
                + "document may assume every URL in it exists.")
            .Produces<DiscoveryDocument>(StatusCodes.Status200OK);

        endpoints.MapGet(JwksPath, Keys)
            .AllowAnonymous()
            .WithName("GetJsonWebKeySet")
            .WithSummary("Public signing keys")
            .WithDescription(
                "The public half of every key a token from this API may carry: the current one, and the "
                + "outgoing one during a rotation overlap. Private material never appears here.")
            .Produces(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>The document, built from what this host can actually do.</summary>
    private static IResult Discovery(HttpContext httpContext)
    {
        string issuer = Issuer(httpContext);

        return Results.Ok(new DiscoveryDocument(
            issuer,
            issuer + JwksPath,
            issuer + OAuth2Endpoints.AuthorizePath,
            issuer + OAuth2TokenEndpoint.TokenPath,
            ["RS256"],
            ["public"],
            ["code"],

            // The two grants SPEC-044 implements. `password` is absent because OAuth 2.1 removes it, and
            // `implicit` because it returns a token through a redirect — both refused, not unimplemented.
            ["authorization_code", "refresh_token"],
            ["S256"]));
    }

    private static IResult Keys(ISigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return Results.Text(
            System.Text.Json.JsonSerializer.Serialize(keys.PublicKeySet()),
            "application/json");
    }

    /// <summary>
    /// The issuer, which is this host's own origin.
    /// </summary>
    /// <remarks>
    /// Taken from the request rather than from configuration, deliberately: the issuer in a discovery
    /// document must match the host the document was fetched from, or a conforming client refuses it. A
    /// configured value would be one more thing to get wrong between localhost, compose and a deployment.
    /// </remarks>
    private static string Issuer(HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
}

/// <summary>
/// The discovery document — OpenID Connect Discovery 1.0 §3, with only the members this provider can honour.
/// </summary>
/// <param name="Issuer">This provider's identifier, which is also the origin the document came from.</param>
/// <param name="JwksUri">Where the public keys are.</param>
/// <param name="AuthorizationEndpoint">Where the code flow starts — SPEC-044. It requires a bearer token,
/// which is this provider's first-party limitation and is recorded in ADR-0031 rather than here: a discovery
/// document has no member for "and the account must already be signed in".</param>
/// <param name="TokenEndpoint">Where a code is exchanged and a session is rotated.</param>
/// <param name="IdTokenSigningAlgValuesSupported">`RS256`, and nothing else — ADR-0031 pinned it.</param>
/// <param name="SubjectTypesSupported">`public`: the `sub` is the same for every client.</param>
/// <param name="ResponseTypesSupported">`code`, and nothing else: no implicit flow and no hybrid.</param>
/// <param name="GrantTypesSupported">`authorization_code` and `refresh_token`.</param>
/// <param name="CodeChallengeMethodsSupported">
/// `S256` only. `plain` is refused, which `docs/api/ENDPOINTS.md` already publishes.
/// </param>
internal sealed record DiscoveryDocument(
    [property: System.Text.Json.Serialization.JsonPropertyName("issuer")] string Issuer,
    [property: System.Text.Json.Serialization.JsonPropertyName("jwks_uri")] string JwksUri,
    [property: System.Text.Json.Serialization.JsonPropertyName("authorization_endpoint")]
    string AuthorizationEndpoint,
    [property: System.Text.Json.Serialization.JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: System.Text.Json.Serialization.JsonPropertyName("id_token_signing_alg_values_supported")]
    IReadOnlyList<string> IdTokenSigningAlgValuesSupported,
    [property: System.Text.Json.Serialization.JsonPropertyName("subject_types_supported")]
    IReadOnlyList<string> SubjectTypesSupported,
    [property: System.Text.Json.Serialization.JsonPropertyName("response_types_supported")]
    IReadOnlyList<string> ResponseTypesSupported,
    [property: System.Text.Json.Serialization.JsonPropertyName("grant_types_supported")]
    IReadOnlyList<string> GrantTypesSupported,
    [property: System.Text.Json.Serialization.JsonPropertyName("code_challenge_methods_supported")]
    IReadOnlyList<string> CodeChallengeMethodsSupported);
