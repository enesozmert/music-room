using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;

/// <summary>
/// Reads the caller's own profile — SDD §13.2 gate 1, the <c>self</c> tier.
/// </summary>
internal sealed class GetOwnProfileQueryHandler : IRequestHandler<GetOwnProfileQuery, OwnProfile?>
{
    private readonly IProfileRepository _profiles;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the profile read path and the caller.</summary>
    /// <param name="profiles">Where profiles live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetOwnProfileQueryHandler(IProfileRepository profiles, ICurrentUserAccessor currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    /// <summary>Reads the profile of whoever is calling.</summary>
    /// <param name="request">The query, which carries nothing.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The owner's full profile, every tier included.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller. The route requires authorisation,
    /// so reaching here without one means the pipeline was bypassed.</exception>
    public Task<OwnProfile?> Handle(GetOwnProfileQuery request, CancellationToken cancellationToken)
    {
        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetOwnProfileQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-011.");

        return _profiles.FindOwnProfileAsync(userId, cancellationToken);
    }
}
