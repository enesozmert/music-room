namespace MusicRoom.Application.Abstractions.Configuration;

/// <summary>
/// Where a secret or a setting comes from — [[ADR-0029]], SPEC-030.
/// </summary>
/// <remarks>
/// One abstraction over every configured value, so a second source is a registration rather than a rewrite.
/// ADR-0029 asked for it before the vault in as many words: *"the abstraction lands earlier, because writing it
/// later means touching every configuration read."*
/// <para>
/// Declared here and implemented in <c>Infrastructure</c>, like every other adapter (ADR-0028). No layer above
/// learns whether a value came from the process environment, a vault, or anywhere else.
/// </para>
/// <para>
/// **A name may be logged. A value may not** — `secrets-management` forbids printing a secret, including
/// during debugging, so <see cref="Require"/> throws a message naming the variable and never what it found.
/// </para>
/// </remarks>
public interface ISecretProvider
{
    /// <summary>
    /// Reads a value the application cannot start without.
    /// </summary>
    /// <param name="name">The variable's name, such as <c>DATABASE_URL</c>.</param>
    /// <returns>The value, which is never blank.</returns>
    /// <exception cref="InvalidOperationException">The value is absent or blank. The message names the
    /// variable and says how to set it; it never carries the value.</exception>
    string Require(string name);

    /// <summary>
    /// Reads a value the application can run without.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>The value, or null when it is absent or blank.</returns>
    /// <remarks>
    /// Blank reads as absent. A host that sets <c>MAIL_PROVIDER=</c> has configured nothing, and the one
    /// place that decides so is here rather than at each call site.
    /// </remarks>
    string? Read(string name);

    /// <summary>
    /// Reads a number, falling back when nothing is configured.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="fallback">What to use when the value is absent.</param>
    /// <returns>The configured number, or the fallback.</returns>
    /// <remarks>
    /// A value that is present and **not** a number stops start-up, which is what it did before this
    /// abstraction existed and is the right answer: a limiter permit written as `abc` is a mistake a host should
    /// hear about at boot rather than silently run without. Every caller already clamps the value it gets — a
    /// non-positive override is ignored, so configuration cannot switch a limit off.
    /// </remarks>
    int ReadInteger(string name, int fallback);

    /// <summary>
    /// Reads a switch, falling back when nothing is configured.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="fallback">What to use when the value is absent.</param>
    /// <returns>The configured switch, or the fallback. A value that is not a boolean stops start-up, as
    /// <see cref="ReadInteger"/> does and for the same reason.</returns>
    /// <remarks>
    /// Here for the same reason as <see cref="ReadInteger"/>: without it the one boolean setting this
    /// application has — the OpenTelemetry exporter of [[ADR-0022]] — would be the single read that still names
    /// a configuration source directly, which is exactly what this abstraction exists to prevent.
    /// </remarks>
    bool ReadBoolean(string name, bool fallback);
}
