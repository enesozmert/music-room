namespace MusicRoom.Domain.Common;

/// <summary>
/// Who may find an event or a playlist — SDD §6.2, subject V.2.1 and V.2.3.
/// </summary>
/// <remarks>
/// Two values, not three: the middle ground a profile field needs (`Friends`) has no meaning for an aggregate
/// whose membership is explicit. A private playlist is found by its owner and its collaborators; a public one
/// by anybody signed in.
/// </remarks>
public enum Visibility
{
    /// <summary>Any authenticated caller may find and read it.</summary>
    Public = 0,

    /// <summary>Only its owner and the people named on it.</summary>
    Private = 1,
}
