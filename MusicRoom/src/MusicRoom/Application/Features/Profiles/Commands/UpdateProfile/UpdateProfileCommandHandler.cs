using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;

/// <summary>
/// Applies the caller's own profile changes — `PATCH /api/v1/me/profile`.
/// </summary>
internal sealed class UpdateProfileCommandHandler
    : IRequestHandler<UpdateProfileCommand, UpdateProfileOutcome>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the profile write path, the caller and the audit context.</summary>
    /// <param name="profiles">Where profiles live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — SPEC-009.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public UpdateProfileCommandHandler(
        IProfileRepository profiles,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _profiles = profiles;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Applies the subset the caller sent.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Updated, or not found.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    /// <remarks>
    /// There is no route that names a user id, and this handler reads none: the only source of identity is the
    /// token, so a caller cannot reach another account's profile even by trying.
    /// </remarks>
    public async Task<UpdateProfileOutcome> Handle(
        UpdateProfileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "UpdateProfileCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-011.");

        _audit.SetTargetId(userId);

        ProfileChanges changes = new(request.RealName, request.Bio, request.Location, request.BirthDate);

        bool updated = await _profiles
            .TryUpdateProfileAsync(userId, changes, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return UpdateProfileOutcome.NotFound;
        }

        return UpdateProfileOutcome.Updated;
    }
}
