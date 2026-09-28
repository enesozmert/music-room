using MusicRoom.Domain.Common;

namespace MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;

/// <summary>
/// A profile as its owner sees it: every field, every tier, and the preference set — subject V.1.
/// </summary>
/// <param name="DisplayName">The handle the account is known by. Always public, and not tiered — SPEC-011
/// escalation 2.</param>
/// <param name="RealName">The real name, or null.</param>
/// <param name="RealNameVisibility">Who may see it.</param>
/// <param name="Bio">Free text, or null.</param>
/// <param name="BioVisibility">Who may see it.</param>
/// <param name="Location">Where the user says they are, or null.</param>
/// <param name="LocationVisibility">Who may see it.</param>
/// <param name="BirthDate">The date of birth, or null.</param>
/// <param name="BirthDateVisibility">Who may see it.</param>
/// <param name="Genres">The genres the user named.</param>
/// <param name="Artists">The artists the user named.</param>
/// <remarks>
/// The owner is the one reader who sees every tier, which is why this shape exists separately from whatever
/// `GET /users/{userId}` will return: that one projects per field against a relationship, and returning this
/// record from it would be the "filter on the client" mistake OWASP API3 names.
/// </remarks>
public sealed record OwnProfile(
    string DisplayName,
    string? RealName,
    ProfileFieldVisibility RealNameVisibility,
    string? Bio,
    ProfileFieldVisibility BioVisibility,
    string? Location,
    ProfileFieldVisibility LocationVisibility,
    DateOnly? BirthDate,
    ProfileFieldVisibility BirthDateVisibility,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Artists);
