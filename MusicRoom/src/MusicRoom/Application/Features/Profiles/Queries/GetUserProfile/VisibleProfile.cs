namespace MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;

/// <summary>
/// One account's profile as a **particular reader** may see it — `GET /api/v1/users/{userId}`.
/// </summary>
/// <param name="UserId">The account this describes.</param>
/// <param name="DisplayName">Always present: the one always-public field (ADR-0034).</param>
/// <param name="RealName">Present only when the reader's relationship clears the field's tier.</param>
/// <param name="Bio">Same.</param>
/// <param name="Location">Same.</param>
/// <param name="BirthDate">Same.</param>
/// <remarks>
/// A field the reader may not see is **null here and absent from the response**, and a reader cannot tell a
/// hidden field from an empty one — that is deliberate. It is also why this record is not
/// <c>OwnProfile</c>: returning the owner's shape and trimming it later is the "filter on the client" mistake
/// OWASP API3 names, and one forgotten branch away from a leak.
/// <para>
/// The values are never read from the database unless the relationship permits them, so a hidden value does not
/// exist in this process at all.
/// </para>
/// </remarks>
public sealed record VisibleProfile(
    Guid UserId,
    string DisplayName,
    string? RealName,
    string? Bio,
    string? Location,
    DateOnly? BirthDate);

/// <summary>
/// What one reader is to one profile's owner.
/// </summary>
/// <remarks>
/// Resolved once per read, from the validated token and the friends graph — never from anything the caller
/// sends.
/// </remarks>
public enum ProfileRelationship
{
    /// <summary>Somebody with no relationship. Sees `Public` fields only.</summary>
    Stranger = 0,

    /// <summary>A friend, per the friends graph. Sees `Public` and `Friends` fields.</summary>
    Friend = 1,

    /// <summary>The owner. Sees everything, including `Private`.</summary>
    Self = 2,
}
