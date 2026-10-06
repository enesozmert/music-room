namespace MusicRoom.Application.Abstractions.Security;

/// <summary>
/// Whether a chosen password is one of the passwords a public breach already published — ADR-0042, SPEC-035.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>Infrastructure</c>, like every other adapter (ADR-0028). No layer above
/// learns that the answer comes from a file compiled into the assembly rather than from a service.
/// <para>
/// **No outbound call, which is the decision rather than a property of the implementation.** ADR-0042 rejected a
/// k-anonymity range query against Have I Been Pwned for three written reasons, the first being that it puts a
/// third party on the registration path. This interface is the seam if that is ever revisited: a second
/// implementation, not a rewrite.
/// </para>
/// <para>
/// One lookup per password choice and nothing per request: an implementation loads its set once and answers from
/// memory.
/// </para>
/// </remarks>
public interface IBreachedPasswordSet
{
    /// <summary>States whether the candidate is in the set.</summary>
    /// <param name="password">The password a caller chose. Compared, never stored, never logged.</param>
    /// <returns><see langword="true"/> when the candidate is a known breached password.</returns>
    /// <remarks>
    /// Case is ignored, because a breach list is a list of strings people chose and the set is the same password
    /// whatever its capitalisation.
    /// </remarks>
    bool Contains(string password);
}
