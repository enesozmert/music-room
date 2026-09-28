namespace MusicRoom.Domain.Common;

/// <summary>
/// Who may see one profile field — subject V.1's three tiers, SDD §6.2.
/// </summary>
/// <remarks>
/// In <c>Domain</c> because it is a domain fact: a different datastore, or no datastore, would still have to
/// answer "who may see this". It is persisted as a <c>smallint</c>, never as text — a stored string is a value
/// nothing validates.
/// </remarks>
public enum ProfileFieldVisibility
{
    /// <summary>Anybody who can read the profile, signed in or not, subject to the endpoint's own gate.</summary>
    Public = 0,

    /// <summary>
    /// The owner and their friends. **Stored but not yet evaluated**: with no friends graph, every reader who
    /// is not the owner is a stranger, so a `Friends` field is currently as hidden as a private one — SPEC-011.
    /// </summary>
    Friends = 1,

    /// <summary>The owner alone.</summary>
    Private = 2,
}
