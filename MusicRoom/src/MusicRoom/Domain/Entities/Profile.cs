using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One account's profile: the fields a user may fill in, each with its own visibility — subject V.1.
/// </summary>
/// <remarks>
/// One row per account, created with the account so no read path has to answer for a missing profile
/// (SPEC-011 escalation 4). Every field starts empty and every tier starts at a stated default — a field whose
/// tier defaulted to public would leak the first time somebody filled it in.
/// <para>
/// <c>DisplayName</c> is deliberately **not** here: it lives on <see cref="User"/>, it is required at
/// registration, and it is always public, because it is the handle a search result and an attendee list must
/// show (SPEC-011 escalation 2).
/// </para>
/// </remarks>
public sealed class Profile : Entity
{
    /// <summary>The account this profile belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The user's real name, or null. Defaults to the friends tier.</summary>
    public string? RealName { get; set; }

    /// <summary>Who may see <see cref="RealName"/>.</summary>
    public ProfileFieldVisibility RealNameVisibility { get; set; } = ProfileFieldVisibility.Friends;

    /// <summary>Free text about the user, or null. Defaults to public: it is written to be read.</summary>
    public string? Bio { get; set; }

    /// <summary>Who may see <see cref="Bio"/>.</summary>
    public ProfileFieldVisibility BioVisibility { get; set; } = ProfileFieldVisibility.Public;

    /// <summary>
    /// Where the user says they are, as free text. **Not a coordinate**: the geofence of ADR-0014 is a
    /// different thing entirely, and this field is never used for a licence check.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>Who may see <see cref="Location"/>.</summary>
    public ProfileFieldVisibility LocationVisibility { get; set; } = ProfileFieldVisibility.Friends;

    /// <summary>The user's date of birth, or null. Defaults to private.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Who may see <see cref="BirthDate"/>.</summary>
    public ProfileFieldVisibility BirthDateVisibility { get; set; } = ProfileFieldVisibility.Private;
}
