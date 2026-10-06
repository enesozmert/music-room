using System.Reflection;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// The breached-password set, compiled into this assembly and loaded once — ADR-0042, SPEC-035.
/// </summary>
/// <remarks>
/// An embedded resource rather than a file read from disk: a file is a deployment step somebody can forget, and
/// a missing screen that boots is worse than a boot that refuses. The constructor throws when the resource is
/// absent, which makes that refusal the behaviour rather than the hope.
/// <para>
/// **The set, measured.** SecLists `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt` at
/// commit `c205c36a445bff37f8e58a9ec829105cd4975c58`, MIT licensed: 999,999 raw entries, of which **46,146**
/// survive lowercasing, de-duplication and the filter to 12 to 64 characters. The other 95.4% are unreachable
/// here — the policy's 12-character minimum already refuses them, so bundling `123456` would buy nothing. The
/// reach that remains is stated beside V6.2.4 and V6.2.12 in `docs/owasp/ASVS-CONFORMANCE.md`.
/// </para>
/// <para>
/// Registered as a singleton: one read of the resource at start-up, one hash lookup per password choice, nothing
/// per request.
/// </para>
/// </remarks>
public sealed class BundledBreachedPasswordSet : IBreachedPasswordSet
{
    /// <summary>
    /// The resource the set is read from.
    /// </summary>
    /// <remarks>
    /// Public so a test can read the same bytes the screen reads and count them, rather than asking this class
    /// how many entries it thinks it loaded.
    /// </remarks>
    public const string DefaultResourceName = "MusicRoom.Infrastructure.Security.breached-passwords.txt";

    private readonly HashSet<string> _entries;

    /// <summary>Loads the bundled set.</summary>
    /// <exception cref="InvalidOperationException">The resource is absent or empty.</exception>
    public BundledBreachedPasswordSet()
        : this(DefaultResourceName)
    {
    }

    /// <summary>Loads a named resource, which is how the absent-resource failure is proven.</summary>
    /// <param name="resourceName">The manifest resource to read.</param>
    /// <exception cref="InvalidOperationException">The resource is absent or empty.</exception>
    /// <remarks>
    /// The name is a parameter because the alternative — trusting that a deployment without the file would throw
    /// — is the assumption this constructor exists to remove. The container uses the parameterless constructor.
    /// </remarks>
    public BundledBreachedPasswordSet(string resourceName) => _entries = Load(resourceName);

    /// <inheritdoc />
    public bool Contains(string password) =>
        !string.IsNullOrEmpty(password) && _entries.Contains(password);

    private static HashSet<string> Load(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        Assembly assembly = typeof(BundledBreachedPasswordSet).Assembly;

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw Missing(resourceName);

        using StreamReader reader = new(stream);

        HashSet<string> entries = new(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is string line)
        {
            if (line.Length > 0)
            {
                entries.Add(line);
            }
        }

        return entries.Count > 0 ? entries : throw Missing(resourceName);
    }

    private static InvalidOperationException Missing(string resourceName) =>
        new($"The breached-password set '{resourceName}' is not embedded in this assembly, or is empty. "
            + "A build that lost it would answer every password choice as unscreened — ADR-0042, SPEC-035.");
}
