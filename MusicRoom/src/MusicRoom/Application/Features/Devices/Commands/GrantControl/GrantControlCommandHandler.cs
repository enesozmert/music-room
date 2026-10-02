using MediatR;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Devices.Commands.GrantControl;

/// <summary>
/// Hands control over, if the caller owns the device, still holds its version, and the grantee is a friend.
/// </summary>
/// <remarks>
/// The three checks run in that order on purpose. Ownership decides whether the caller may be told anything at
/// all, so it comes first: a non-owner receives the `404` a stranger receives, rather than a `422` that would
/// confirm the device exists and describe the caller's own friends graph back to them.
/// </remarks>
internal sealed class GrantControlCommandHandler
    : IRequestHandler<GrantControlCommand, DeviceWriteOutcome>
{
    private readonly IDeviceRepository _devices;
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;

    /// <summary>Takes the write path, the friends graph, the caller and the audit context.</summary>
    /// <param name="devices">Where devices live.</param>
    /// <param name="friendships">Where the friendship rule is checked — SPEC-012's graph.</param>
    /// <param name="currentUser">Who is delegating, from the validated token.</param>
    /// <param name="audit">Where this handler states what became of the request — ADR-0033.</param>
    /// <remarks>
    /// Public on an internal type: MediatR activates the handler through a public constructor only.
    /// </remarks>
    public GrantControlCommandHandler(
        IDeviceRepository devices,
        IFriendshipRepository friendships,
        ICurrentUserAccessor currentUser,
        IAuditContext audit)
    {
        _devices = devices;
        _friendships = friendships;
        _currentUser = currentUser;
        _audit = audit;
    }

    /// <summary>Grants it, or reports why not.</summary>
    /// <param name="request">The validated command.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `200`, `412`, `422` or `404`.</returns>
    /// <exception cref="InvalidOperationException">No authenticated caller.</exception>
    public async Task<DeviceWriteOutcome> Handle(
        GrantControlCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid ownerId = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "GrantControlCommand reached its handler with no authenticated caller. The route requires "
                + "authorisation — SPEC-020.");

        _audit.SetTargetId(request.DeviceId);

        // Ownership first. A caller who does not own the device gets the same `404` a stranger gets, and learns
        // nothing — not even whether their intended grantee is a friend of theirs. Only once the caller is
        // established as the owner is the payload worth ruling on.
        DeviceControl? control = await _devices
            .FindControlAsync(request.DeviceId, ownerId, cancellationToken)
            .ConfigureAwait(false);

        if (control?.OwnerId != ownerId)
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return DeviceWriteOutcome.NotFound;
        }

        // Then the rule, with a `422` that names it: the owner is entitled to know why the grantee was refused.
        // Granting to oneself is caught here too — nobody is their own friend in this graph.
        if (!await _friendships
                .AreFriendsAsync(ownerId, request.GranteeUserId, cancellationToken)
                .ConfigureAwait(false))
        {
            _audit.SetOutcome(AuditOutcome.Refused);

            return DeviceWriteOutcome.NotAFriend;
        }

        DeviceWriteOutcome outcome = await _devices
            .TryGrantControlAsync(
                request.DeviceId,
                ownerId,
                request.GranteeUserId,
                request.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome != DeviceWriteOutcome.Written)
        {
            _audit.SetOutcome(AuditOutcome.Refused);
        }

        return outcome;
    }
}
