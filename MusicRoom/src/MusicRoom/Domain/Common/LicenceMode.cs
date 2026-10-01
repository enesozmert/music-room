namespace MusicRoom.Domain.Common;

/// <summary>
/// Who may write to an aggregate — SDD §6.2, subject V.2.1 and V.2.3.
/// </summary>
/// <remarks>
/// A **playlist** accepts `Everyone` and `InvitedOnly` only; `LocationAndTimeRestricted` is `422` there,
/// because subject V.2.1 attaches location-and-time to an event and ADR-0014's geofence and event code are
/// event concepts. Accepting the value on a playlist would imply a check nothing implements.
/// </remarks>
public enum LicenceMode
{
    /// <summary>Anybody who may see it may write to it.</summary>
    Everyone = 0,

    /// <summary>Only the people named on it, which for a playlist means collaborators with `canEdit`.</summary>
    InvitedOnly = 1,

    /// <summary>
    /// Only people inside a geofence and a time window, proving presence — ADR-0014. **Events only.**
    /// </summary>
    LocationAndTimeRestricted = 2,
}
