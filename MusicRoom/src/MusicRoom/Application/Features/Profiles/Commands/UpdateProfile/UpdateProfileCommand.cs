using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;

/// <summary>
/// Updates a subset of the caller's own profile fields — `PATCH /api/v1/me/profile`.
/// </summary>
/// <param name="RealName">The new real name and its tier, or null to leave the field alone.</param>
/// <param name="Bio">The new bio and its tier, or null to leave it alone.</param>
/// <param name="Location">The new location and its tier, or null to leave it alone.</param>
/// <param name="BirthDate">The new date of birth and its tier, or null to leave it alone.</param>
/// <remarks>
/// A <c>PATCH</c>: an absent field is untouched, not cleared. Clearing a field is sending it with a null value
/// and a tier, which is why the value inside <see cref="ProfileField{T}"/> is nullable and the tier is not.
/// </remarks>
public sealed record UpdateProfileCommand(
    ProfileField<string>? RealName,
    ProfileField<string>? Bio,
    ProfileField<string>? Location,
    ProfileDateField? BirthDate) : IRequest<UpdateProfileOutcome>, IAuditableCommand
{
    /// <inheritdoc />
    /// <remarks>A stable string, not a type name: renaming this record must not rewrite history.</remarks>
    public string AuditAction => "profile.update";

    /// <inheritdoc />
    public string AuditTargetType => "profile";
}

/// <summary>
/// One field's new value and the tier that governs it.
/// </summary>
/// <typeparam name="T">The field's type.</typeparam>
/// <param name="Value">The value, or null to clear the field while keeping a tier.</param>
/// <param name="Visibility">Who may see it. **Required** — `docs/api/ENDPOINTS.md`: "a missing tier is `422`,
/// never a silent default to public".</param>
/// <remarks>
/// Value and tier travel together so a field cannot exist without one. That is the contract's own rule, and it
/// is enforced here by the type rather than by a validator anybody could forget to write.
/// </remarks>
public sealed record ProfileField<T>(T? Value, ProfileFieldVisibility Visibility);

/// <summary>
/// The date-of-birth field: a nullable date and the tier that governs it.
/// </summary>
/// <param name="Value">The date, or null to clear the field while keeping a tier.</param>
/// <param name="Visibility">Who may see it. Required, for the same reason as every other field's.</param>
/// <remarks>
/// Its own record rather than <see cref="ProfileField{T}"/> closed over a value type: an unconstrained
/// <c>T?</c> is "maybe-null reference", not <c>Nullable&lt;DateOnly&gt;</c>.
/// </remarks>
public sealed record ProfileDateField(DateOnly? Value, ProfileFieldVisibility Visibility);

/// <summary>What an update did.</summary>
public enum UpdateProfileOutcome
{
    /// <summary>The account has no profile row, which should be unreachable — see <c>IProfileRepository</c>.</summary>
    NotFound = 0,

    /// <summary>The fields the caller sent were applied.</summary>
    Updated = 1,
}

/// <summary>
/// The subset of fields a repository must apply, as the handler resolved them.
/// </summary>
/// <param name="RealName">The real name change, or null.</param>
/// <param name="Bio">The bio change, or null.</param>
/// <param name="Location">The location change, or null.</param>
/// <param name="BirthDate">The birth-date change, or null.</param>
/// <remarks>
/// Declared beside the command rather than in its own file: it is the command's own shape restated for the
/// write path, and SPEC-011's Files table is the contract for what this change may create.
/// </remarks>
public sealed record ProfileChanges(
    ProfileField<string>? RealName,
    ProfileField<string>? Bio,
    ProfileField<string>? Location,
    ProfileDateField? BirthDate);
