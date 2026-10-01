using Microsoft.EntityFrameworkCore;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;
using MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;
using MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;
using MusicRoom.Application.Features.Profiles.Queries.SearchUsers;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;

namespace MusicRoom.Persistence.Repositories;

/// <summary>
/// The EF Core read and write paths for a profile.
/// </summary>
internal sealed class ProfileRepository : IProfileRepository
{
    private readonly MusicRoomDbContext _context;

    /// <summary>Takes the scoped context.</summary>
    /// <param name="context">The unit of work for this request.</param>
    /// <remarks>
    /// Public on an internal type: the container activates a registered implementation through a public
    /// constructor only.
    /// </remarks>
    public ProfileRepository(MusicRoomDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<OwnProfile?> FindOwnProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        ProfileRow? row = await ReadRowAsync(userId, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // Two reads rather than a join that would repeat every profile column once per preference row.
        List<MusicPreference> preferences = await _context.MusicPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .OrderBy(preference => preference.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OwnProfile(
            row.DisplayName,
            row.RealName,
            row.RealNameVisibility,
            row.Bio,
            row.BioVisibility,
            row.Location,
            row.LocationVisibility,
            row.BirthDate,
            row.BirthDateVisibility,
            Values(preferences, MusicPreferenceKind.Genre),
            Values(preferences, MusicPreferenceKind.Artist));
    }

    /// <summary>
    /// Projects the profile and the account's display name in one query. The display name lives on
    /// <c>users</c> because it is always public and required at registration — SPEC-011 escalation 2.
    /// </summary>
    private Task<ProfileRow?> ReadRowAsync(Guid userId, CancellationToken cancellationToken) =>
        _context.Profiles
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => new ProfileRow(
                _context.Users
                    .Where(user => user.Id == userId)
                    .Select(user => user.DisplayName)
                    .First(),
                candidate.RealName,
                candidate.RealNameVisibility,
                candidate.Bio,
                candidate.BioVisibility,
                candidate.Location,
                candidate.LocationVisibility,
                candidate.BirthDate,
                candidate.BirthDateVisibility))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryUpdateProfileAsync(
        Guid userId,
        ProfileChanges changes,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Tracked rather than an ExecuteUpdate: a PATCH touches an arbitrary subset, and expressing "these four
        // maybe-columns" as one setter chain is less readable than letting the change tracker do it.
        Profile? profile = await _context.Profiles
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return false;
        }

        Apply(profile, changes);

        profile.UpdatedAtUtc = nowUtc;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task ReplaceMusicPreferencesAsync(
        Guid userId,
        IReadOnlyList<MusicPreference> preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        // PUT replaces the set, so the old rows go. Both statements run in the transaction the audit behaviour
        // already opened, so a caller never sees a half-replaced set.
        await _context.MusicPreferences
            .Where(preference => preference.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (preferences.Count > 0)
        {
            _context.MusicPreferences.AddRange(preferences);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<VisibleProfile?> FindVisibleProfileAsync(
        Guid userId,
        ProfileRelationship relationship,
        CancellationToken cancellationToken)
    {
        // The highest tier this reader clears. Public is 0 and Private is 2, so "may see" is "tier <= permitted".
        ProfileFieldVisibility permitted = relationship switch
        {
            ProfileRelationship.Self => ProfileFieldVisibility.Private,
            ProfileRelationship.Friend => ProfileFieldVisibility.Friends,
            _ => ProfileFieldVisibility.Public,
        };

        // Projected in SQL, field by field: a value the reader may not see is replaced by null **before** it
        // leaves the database, so it never exists in this process — OWASP API3, and the reason this is not a
        // filter over OwnProfile.
        return _context.Profiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => new VisibleProfile(
                userId,
                _context.Users
                    .Where(user => user.Id == userId)
                    .Select(user => user.DisplayName)
                    .First(),
                profile.RealNameVisibility <= permitted ? profile.RealName : null,
                profile.BioVisibility <= permitted ? profile.Bio : null,
                profile.LocationVisibility <= permitted ? profile.Location : null,
                profile.BirthDateVisibility <= permitted ? profile.BirthDate : null))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSearchResult>> SearchByDisplayNameAsync(
        string displayNamePrefix,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            // A case-insensitive prefix match through ILIKE. `StartsWith(..., OrdinalIgnoreCase)` does not
            // translate — "The LINQ expression ... could not be translated" — and doing the comparison in memory
            // would mean reading every account to answer a search box.
            //
            // The pattern is escaped, so a caller cannot smuggle `%` or `_` in and turn a prefix search into a
            // full scan or a wildcard probe.
            .Where(user => EF.Functions.ILike(user.DisplayName, EscapeLikePattern(displayNamePrefix) + "%", "\\"))
            .OrderBy(user => user.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserSearchResult(user.Id, user.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Applies only the fields the caller sent; an absent field is untouched, which is what PATCH means.</summary>
    private static void Apply(Profile profile, ProfileChanges changes)
    {
        if (changes.RealName is not null)
        {
            profile.RealName = changes.RealName.Value;
            profile.RealNameVisibility = changes.RealName.Visibility;
        }

        if (changes.Bio is not null)
        {
            profile.Bio = changes.Bio.Value;
            profile.BioVisibility = changes.Bio.Visibility;
        }

        if (changes.Location is not null)
        {
            profile.Location = changes.Location.Value;
            profile.LocationVisibility = changes.Location.Visibility;
        }

        if (changes.BirthDate is not null)
        {
            profile.BirthDate = changes.BirthDate.Value;
            profile.BirthDateVisibility = changes.BirthDate.Visibility;
        }
    }

    /// <summary>
    /// Escapes the three characters <c>ILIKE</c> treats as syntax, so a search term is only ever a literal.
    /// </summary>
    /// <remarks>
    /// Without this, a query of <c>%</c> matches every account and <c>_</c> matches any single character — a
    /// wildcard probe with a search box in front of it.
    /// </remarks>
    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static List<string> Values(List<MusicPreference> preferences, MusicPreferenceKind kind) =>
        preferences
            .Where(preference => preference.Kind == kind)
            .Select(preference => preference.Value)
            .ToList();
}

/// <summary>
/// One profile row plus the account's display name, as the database returns it.
/// </summary>
/// <param name="DisplayName">From <c>users</c>: always public, never tiered.</param>
/// <param name="RealName">The real name, or null.</param>
/// <param name="RealNameVisibility">Who may see it.</param>
/// <param name="Bio">The bio, or null.</param>
/// <param name="BioVisibility">Who may see it.</param>
/// <param name="Location">The location, or null.</param>
/// <param name="LocationVisibility">Who may see it.</param>
/// <param name="BirthDate">The date of birth, or null.</param>
/// <param name="BirthDateVisibility">Who may see it.</param>
/// <remarks>
/// Declared beside the repository rather than in its own file: it exists only as the shape of one query, and
/// SPEC-011's Files table is the contract for what this change may create.
/// </remarks>
internal sealed record ProfileRow(
    string DisplayName,
    string? RealName,
    ProfileFieldVisibility RealNameVisibility,
    string? Bio,
    ProfileFieldVisibility BioVisibility,
    string? Location,
    ProfileFieldVisibility LocationVisibility,
    DateOnly? BirthDate,
    ProfileFieldVisibility BirthDateVisibility);
