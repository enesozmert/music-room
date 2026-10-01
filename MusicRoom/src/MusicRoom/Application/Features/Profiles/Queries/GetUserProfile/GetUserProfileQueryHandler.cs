using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Profiles.Queries.GetUserProfile;

/// <summary>
/// Resolves the reader's relationship to the owner, then reads only what that relationship permits —
/// SDD §13.2 gate 2, subject V.1's three tiers.
/// </summary>
internal sealed class GetUserProfileQueryHandler : IRequestHandler<GetUserProfileQuery, VisibleProfile>
{
    private readonly IProfileRepository _profiles;
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the profile read path, the friends graph and the caller.</summary>
    /// <param name="profiles">Where profiles live.</param>
    /// <param name="friendships">What decides the `Friends` tier.</param>
    /// <param name="currentUser">Who is reading, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GetUserProfileQueryHandler(
        IProfileRepository profiles,
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser)
    {
        _profiles = profiles;
        _friendships = friendships;
        _currentUser = currentUser;
    }

    /// <summary>Reads the profile as this reader may see it.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// The visible projection. An account that does not exist answers the same sparse shape as one that has
    /// hidden everything, so no caller learns which accounts exist by asking — SPEC-013 decision 2.
    /// </returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<VisibleProfile> Handle(
        GetUserProfileQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid readerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GetUserProfileQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-013.");

        ProfileRelationship relationship = await ResolveAsync(readerId, request.UserId, cancellationToken)
            .ConfigureAwait(false);

        VisibleProfile? visible = await _profiles
            .FindVisibleProfileAsync(request.UserId, relationship, cancellationToken)
            .ConfigureAwait(false);

        // An unknown account and an account whose owner hid everything answer alike: a display name the reader
        // already typed, and nothing else.
        return visible ?? new VisibleProfile(
            request.UserId,
            DisplayName: string.Empty,
            RealName: null,
            Bio: null,
            Location: null,
            BirthDate: null);
    }

    /// <summary>
    /// Self, friend or stranger — resolved from the token and the graph, never from the request.
    /// </summary>
    private async Task<ProfileRelationship> ResolveAsync(
        Guid readerId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (readerId == ownerId)
        {
            return ProfileRelationship.Self;
        }

        bool friends = await _friendships
            .AreFriendsAsync(readerId, ownerId, cancellationToken)
            .ConfigureAwait(false);

        return friends ? ProfileRelationship.Friend : ProfileRelationship.Stranger;
    }
}
