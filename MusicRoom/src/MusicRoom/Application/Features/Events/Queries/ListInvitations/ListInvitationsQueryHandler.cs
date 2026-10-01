using MediatR;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Events.Queries.ListInvitations;

/// <summary>
/// Reads the invitation list for the owner, and answers null for anybody else.
/// </summary>
internal sealed class ListInvitationsQueryHandler
    : IRequestHandler<ListInvitationsQuery, IReadOnlyList<InvitationSummary>?>
{
    private readonly IEventRepository _events;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Takes the read path and the caller.</summary>
    /// <param name="events">Where events live.</param>
    /// <param name="currentUser">Who is asking, from the validated token.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public ListInvitationsQueryHandler(IEventRepository events, ICurrentUserAccessor currentUser)
    {
        _events = events;
        _currentUser = currentUser;
    }

    /// <summary>Lists them, or answers null when the caller does not own the event.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The invitations, or null.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<IReadOnlyList<InvitationSummary>?> Handle(
        ListInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "ListInvitationsQuery reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-017.");

        EventAccess access = await _events
            .FindAccessAsync(request.EventId, userId, DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!access.IsOwner)
        {
            return null;
        }

        return await _events
            .ListInvitationsAsync(request.EventId, userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
