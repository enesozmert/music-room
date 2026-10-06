namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The context-word screen: a password may not contain the application's name, the caller's address or their
/// display name — ADR-0042, SPEC-035.
/// </summary>
/// <remarks>
/// Stated once and called from three places, which is SPEC-034's lesson applied before the duplication rather
/// than after it: register screens in its validator because its command carries both values, while reset and
/// change screen in their handlers because theirs do not.
/// <para>
/// **Containment, not equality.** `musicroom2026` is the case ADR-0042 named: an equality test would accept it
/// and a length rule already does.
/// </para>
/// <para>
/// No method here returns which word matched, and the refusal message does not either. Naming the word would
/// confirm a display name or an address to whoever submitted the request.
/// </para>
/// <para>
/// **Public, where the spec's Files-table walk said internal.** The walk was right that every caller is in this
/// assembly and wrong about the proof: `PasswordContextTests` lives in `MusicRoom.Application.Tests`, which sees
/// only public types, and the Files table the owner approved names that suite. SPEC-034 made the same correction
/// for the same reason one unit earlier. <see cref="Normalise"/> stays internal — nothing outside this assembly
/// compares anything.
/// </para>
/// </remarks>
public static class PasswordContext
{
    /// <summary>
    /// The shortest context word that refuses anything.
    /// </summary>
    /// <remarks>
    /// Three characters and under are dropped: a display name of `Al` would otherwise refuse every password
    /// containing those two letters, and `one` would refuse `moneylender`. ADR-0042 fixes the floor at four.
    /// </remarks>
    internal const int MinimumWordLength = 4;

    /// <summary>
    /// The project's own words, which belong to no account and are screened on every path.
    /// </summary>
    /// <remarks>
    /// The host name joins this list when a deployment exists — ADR-0042, issue O11. `music room` normalises to
    /// the same word as `musicroom`, and it is kept because the list is read by people as well as by the
    /// screen.
    /// </remarks>
    private static readonly string[] _projectWords = ["musicroom", "music room", "musicroomapp"];

    /// <summary>
    /// The characters a caller puts between words and an attacker's dictionary does not.
    /// </summary>
    /// <remarks>
    /// Stripped from both sides of the comparison, so `Music-Room!` is screened as `musicroom!`. The exclamation
    /// mark stays: it is not a separator, and removing every non-letter would turn `p4ssword` into something the
    /// caller did not type.
    /// </remarks>
    private static readonly char[] _separators = [' ', '\t', '.', '_', '-'];

    /// <summary>
    /// States whether a chosen password contains a context word.
    /// </summary>
    /// <param name="password">The password being chosen.</param>
    /// <param name="email">The account's address, or null when this path cannot reach it.</param>
    /// <param name="displayName">The account's display name, or null for the same reason.</param>
    /// <returns><see langword="true"/> when the password must be refused.</returns>
    /// <remarks>
    /// A null address and a null display name leave the project's own words, which still refuse `musicroom2026`.
    /// That is what the reset path falls back to when its token matches no account — the write path answers that
    /// case, not this screen.
    /// </remarks>
    public static bool ContainsAContextWord(string? password, string? email, string? displayName)
    {
        string candidate = Normalise(password);

        if (candidate.Length == 0)
        {
            return false;
        }

        foreach (string word in Words(email, displayName))
        {
            if (word.Length >= MinimumWordLength && candidate.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lowercases and removes the separators, for both sides of every comparison.
    /// </summary>
    /// <param name="value">Anything being compared, including the password.</param>
    /// <returns>The comparable form, or an empty string for null.</returns>
    /// <remarks>
    /// The invariant culture rather than the current one: a screen whose answer depends on the server's locale
    /// is a screen that refuses different passwords on different hosts.
    /// </remarks>
    internal static string Normalise(string? value) =>
        value is null
            ? string.Empty
            : string.Concat(value.ToLowerInvariant().Split(_separators, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> Words(string? email, string? displayName)
    {
        foreach (string projectWord in _projectWords)
        {
            yield return Normalise(projectWord);
        }

        yield return Normalise(LocalPart(email));
        yield return Normalise(displayName);
    }

    private static string LocalPart(string? email)
    {
        if (email is null)
        {
            return string.Empty;
        }

        int at = email.IndexOf('@', StringComparison.Ordinal);

        return at < 0 ? email : email[..at];
    }
}
