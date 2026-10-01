using System.Security.Cryptography;

namespace MusicRoom.Application.Features.Events.Presence;

/// <summary>
/// Generates the code an owner reads out at the venue — ADR-0014.
/// </summary>
/// <remarks>
/// Eight characters from a 32-symbol alphabet is 40 bits, which is far more than a code that lives for minutes
/// and is rate limited needs, and short enough to read off a screen. The alphabet omits `I`, `L`, `O`, `U`, `0`
/// and `1`: a code is transcribed by a human in a dark room, and a character set that invites a typo invites a
/// support problem that looks like a security problem.
/// <para>
/// <see cref="RandomNumberGenerator"/> and not <c>Random</c>: this is a credential, and a predictable one votes.
/// </para>
/// </remarks>
public static class EventCodeGenerator
{
    /// <summary>How many characters a code has.</summary>
    public const int Length = 8;

    /// <summary>
    /// The alphabet, without the characters a human confuses — no `I`, `L`, `O`, `U`, `0` or `1`.
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>A new code, in plaintext. It is shown once and stored only as a digest.</summary>
    /// <returns>The code.</returns>
    public static string Create() =>
        RandomNumberGenerator.GetString(Alphabet, Length);
}
