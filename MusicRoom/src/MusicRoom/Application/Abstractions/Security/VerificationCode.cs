using System.Security.Cryptography;

namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// The six-character code a person types instead of clicking the link — SPEC-052.
/// </summary>
/// <remarks>
/// **Thirty-two symbols, not thirty-six.** `I` and `O` are removed because `1` and `0` are, and those two
/// pairs are what a person transcribes wrongly from a phone screen. The cost is one bit per character; the
/// benefit is a code that can be read aloud and typed once.
/// <para>
/// **The entropy is not what makes this safe.** 32⁶ is about 1.07 billion, which a determined attacker would
/// walk through given unlimited attempts. What makes it safe is the attempt budget on the record it belongs
/// to — five wrong codes spend it — and the per-IP and per-account limits already in front of the endpoints
/// that issue one. A short code without a budget is a liability; this file only mints the code, and
/// <c>IUserRepository</c> holds the budget.
/// </para>
/// <para>
/// **The code is stored as a digest**, like every other credential in this system: hashed with
/// <see cref="OpaqueTokenHash" />, so a leaked database hands nobody a working code.
/// </para>
/// </remarks>
public static class VerificationCode
{
    /// <summary>How many characters a person types.</summary>
    public const int Length = 6;

    /// <summary>
    /// The alphabet: `A` to `Z` without `I` and `O`, and `2` to `9`.
    /// </summary>
    /// <remarks>
    /// Thirty-two symbols exactly, which also means each character is five bits and the generator can take
    /// them without the modulo bias an awkward alphabet size introduces.
    /// </remarks>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Mints a fresh code.</summary>
    /// <returns>Six characters from <see cref="Alphabet" />.</returns>
    /// <remarks>
    /// <see cref="RandomNumberGenerator.GetString" /> draws uniformly from the alphabet, so no character is
    /// more likely than another — the thing a hand-rolled `% length` would quietly get wrong.
    /// </remarks>
    public static string Create() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// Puts a submitted code into the shape the digest was taken of.
    /// </summary>
    /// <param name="submitted">What the caller typed, which may carry spaces, dashes or lower case.</param>
    /// <returns>Upper case, with every character that is not in the alphabet removed.</returns>
    /// <remarks>
    /// A person copying a code out of a mail client brings a trailing space, and one writing it down adds a
    /// dash. Refusing those would be refusing a correct code for a reason nobody can see, so they are
    /// stripped here — once, on the way in, rather than by each caller.
    /// </remarks>
    public static string Normalise(string submitted)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        return string.Concat(submitted
            .ToUpperInvariant()
            .Where(character => Alphabet.Contains(character, StringComparison.Ordinal)));
    }

    /// <summary>Whether a submitted value could be a code at all.</summary>
    /// <param name="normalised">The output of <see cref="Normalise" />.</param>
    /// <returns>True when it is exactly <see cref="Length" /> characters of the alphabet.</returns>
    /// <remarks>
    /// Checked before the database is asked, so a caller sending a sentence costs a comparison rather than
    /// a query — and so the attempt budget is spent by guesses at a code, not by malformed input.
    /// </remarks>
    public static bool IsWellFormed(string normalised) =>
        normalised is { Length: Length } && normalised.All(Alphabet.Contains);
}
