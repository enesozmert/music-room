using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Features.Profiles.Commands.ReplaceMusicPreferences;

/// <summary>
/// Replaces the caller's preference set — subject V.1, and the storing half of issue O14.
/// </summary>
internal sealed class ReplaceMusicPreferencesCommandHandler : IRequestHandler<ReplaceMusicPreferencesCommand>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the profile write path, the caller and the audit context.</summary>
    /// <param name="profiles">Where preferences live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ReplaceMusicPreferencesCommandHandler(
        IProfileRepository profiles,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _profiles = profiles;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Replaces the set with what the caller sent.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task Handle(ReplaceMusicPreferencesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ReplaceMusicPreferencesCommand reached its handler with no authenticated caller. The route "
                + "requires authorisation — SPEC-011.");

        _audit.SetTargetId(userId);

        DateTime nowUtc = DateTime.UtcNow;

        List<MusicPreference> preferences =
        [
            .. Rows(userId, request.Genres, MusicPreferenceKind.Genre, nowUtc),
            .. Rows(userId, request.Artists, MusicPreferenceKind.Artist, nowUtc),
        ];

        await _profiles
            .ReplaceMusicPreferencesAsync(userId, preferences, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns one kind's values into rows, trimmed and de-duplicated.
    /// </summary>
    /// <remarks>
    /// De-duplicated here rather than left to the unique index: "rock, Rock" is one preference typed twice, and
    /// answering `409` for it would be pedantry rather than protection.
    /// </remarks>
    private static IEnumerable<MusicPreference> Rows(
        Guid userId,
        IReadOnlyList<string> values,
        MusicPreferenceKind kind,
        DateTime nowUtc) =>
        values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new MusicPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = kind,
                Value = value,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            });
}
