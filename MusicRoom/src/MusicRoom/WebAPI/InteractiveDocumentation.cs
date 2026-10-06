namespace MusicRoom.WebAPI;

/// <summary>
/// The interactive renderer for the document the API already publishes — ADR-0035, SPEC-032.
/// </summary>
/// <remarks>
/// Its own class, in its own file, for the two reasons the build gave when the registration sat inline:
/// `Program.cs` is at S104's 300-line cap, and naming the UI middleware from `Main` put its coupling at 42
/// against CA1506's 41. It is the shape <c>ApiServices</c>, <c>RealtimeServices</c>, <c>Observability</c> and
/// <c>InfrastructureSetup</c> already use.
/// <para>
/// **One API, one generator.** <c>AddOpenApi()</c> produces the document and this renders it; `SwaggerGen` is
/// deliberately absent, so nothing here can describe the API a second time and drift from the committed
/// <c>docs/api/openapi.json</c>.
/// </para>
/// </remarks>
internal static class InteractiveDocumentation
{
    /// <summary>
    /// Where the generated document is served — the path <c>MapOpenApi()</c> publishes and the UI reads.
    /// </summary>
    /// <remarks>
    /// Named once so the UI configuration and the test that fetches it cannot disagree about it.
    /// </remarks>
    internal const string DocumentPath = "/openapi/v1.json";

    /// <summary>Where the page is served, when it is served at all.</summary>
    internal const string UiPath = "/swagger/index.html";

    /// <summary>The title the page shows above the document.</summary>
    internal const string DocumentTitle = "MusicRoom v1";

    /// <summary>
    /// Serves the document in every environment, and the UI on a Development host only.
    /// </summary>
    /// <param name="app">The application being built.</param>
    /// <remarks>
    /// Development only, because an interactive, unauthenticated catalogue of every endpoint is an
    /// attack-surface map — ADR-0035, and API8 in <c>docs/owasp/API-TOP10-CHECKLIST.md</c> calls a
    /// documentation UI shipped to production the textbook misconfiguration. Nothing is lost: the audience is
    /// two developers and the defence walkthrough, and both run the local stack.
    /// <para>
    /// <c>MapOpenApi()</c> is called here too, and **outside** the branch: `GET /openapi/v1.json` keeps
    /// answering in every environment exactly as it did before this unit, which is what ADR-0035 preserved and
    /// what the third test asserts on both hosts. Both registrations live in one method because `Program.cs`
    /// is at S104's cap and SPEC-032's answer 5 forbids moving anything else out of it — one call there replaces
    /// the one it already had, so the composition root is no longer than before.
    /// </para>
    /// </remarks>
    internal static void Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Every environment, as before — openapi-documentation, and the contract CI drift-checks.
        app.MapOpenApi();

        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.UseSwaggerUI(options => options.SwaggerEndpoint(DocumentPath, DocumentTitle));
    }
}
