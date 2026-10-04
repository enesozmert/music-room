namespace MusicRoom.WebAPI.Hubs;

/// <summary>
/// What a realtime client may hold and how fast it may call — [[ADR-0030]], numbers set by the repository owner
/// on 2026-10-04.
/// </summary>
/// <param name="ConnectionsPerUser">How many connections one account may hold at once.</param>
/// <param name="ConnectionsPerAddress">How many connections one address may hold at once.</param>
/// <param name="InvocationsPerConnectionPerMinute">How many hub methods one connection may invoke a minute.</param>
/// <param name="AllowedOrigins">The origins a browser handshake may come from. Empty means none.</param>
/// <remarks>
/// The defaults <em>are</em> the decision, so a host with no override still limits at the approved numbers — the
/// same posture the credential limits take. Configuration may raise or lower each, which is what lets a Gatling
/// run answer ADR-0030's own question about whether sixty is generous or tight; a non-positive override is
/// ignored, because configuration must not be able to switch a ceiling off.
/// </remarks>
public sealed record HubConnectionLimits(
    int ConnectionsPerUser,
    int ConnectionsPerAddress,
    int InvocationsPerConnectionPerMinute,
    IReadOnlyList<string> AllowedOrigins)
{
    /// <summary>A phone, a tablet, and a dropped connection the server has not reaped yet.</summary>
    public const int DefaultConnectionsPerUser = 5;

    /// <summary>The venue case: a shared network at an event.</summary>
    public const int DefaultConnectionsPerAddress = 20;

    /// <summary>One a second sustained, against a client that joins a group and then mostly listens.</summary>
    public const int DefaultInvocationsPerConnectionPerMinute = 60;

    /// <summary>The window the invocation limit counts in.</summary>
    public static readonly TimeSpan InvocationWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether a handshake carrying this origin may proceed.
    /// </summary>
    /// <param name="origin">The <c>Origin</c> header, or null when there is none.</param>
    /// <returns>True when the handshake is allowed.</returns>
    /// <remarks>
    /// **No origin passes.** That is the React Native client, which is not a browser and sends none — a
    /// deliberate hole ADR-0030 records rather than an oversight, with the JWT as the control standing behind
    /// it. Any non-browser client can omit the header, so this check closes the browser path and nothing else.
    /// <para>
    /// An origin that is present is matched exactly and case-insensitively against the list: a scheme and host
    /// comparison that tried to be clever is how `https://evil-example.com` gets admitted by a rule written for
    /// `https://example.com`.
    /// </para>
    /// </remarks>
    public bool AllowsOrigin(string? origin) =>
        string.IsNullOrWhiteSpace(origin)
        || AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
}
