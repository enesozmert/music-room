namespace MusicRoom.Application.Abstractions.Realtime;

/// <summary>
/// One fact about one person's control of one device — SDD §9.2, `ControlHub`.
/// </summary>
/// <param name="RecipientUserId">
/// Who is being told. It is also the group key: `ControlHub` groups by <c>user:{userId}</c>, so the aggregate a
/// control fact is addressed to is a **person** rather than a shared thing — SPEC-022 decision 1.
/// </param>
/// <param name="DeviceId">The device whose control moved.</param>
/// <param name="OccurredAtUtc">Server time when the change committed.</param>
/// <remarks>
/// No row version — ADR-0039's rule. A device's `xmin` is the `If-Match` of its claim route, and a listener who
/// never holds that token would learn nothing from it. The authoritative value is who controls the device now,
/// which each payload states outright, and `GET /api/v1/devices/{deviceId}/control` is the reconcile point.
/// </remarks>
public abstract record ControlBroadcast(Guid RecipientUserId, Guid DeviceId, DateTime OccurredAtUtc)
    : IRealtimeBroadcast
{
    /// <inheritdoc />
    public Guid AggregateId => RecipientUserId;
}

/// <summary>Control of a device was handed to somebody — `ControlGranted`.</summary>
/// <param name="RecipientUserId">Who is being told: the grantee, and separately the owner.</param>
/// <param name="DeviceId">The device.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="OwnerId">Who owns the device.</param>
/// <param name="ControllerUserId">Who holds control now.</param>
/// <remarks>
/// Sent to the grantee **and** to the owner — SDD §10.5 step 4. The owner's other clients are as wrong as the
/// grantee's until they hear: the one that made the call received the `200`, and a phone in another pocket did
/// not.
/// </remarks>
public sealed record ControlGranted(
    Guid RecipientUserId,
    Guid DeviceId,
    DateTime OccurredAtUtc,
    Guid OwnerId,
    Guid ControllerUserId) : ControlBroadcast(RecipientUserId, DeviceId, OccurredAtUtc);

/// <summary>A device went back to its owner's control — `ControlRevoked`.</summary>
/// <param name="RecipientUserId">Who is being told: the previous controller, and separately the owner.</param>
/// <param name="DeviceId">The device.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="OwnerId">Who owns it, and therefore who controls it again.</param>
/// <remarks>
/// One event for both the owner's revoke and the controller's release: the two intents have the same effect, and
/// a listener needs to know the state rather than which button produced it.
/// </remarks>
public sealed record ControlRevoked(
    Guid RecipientUserId,
    Guid DeviceId,
    DateTime OccurredAtUtc,
    Guid OwnerId) : ControlBroadcast(RecipientUserId, DeviceId, OccurredAtUtc);

/// <summary>Somebody else now holds control that this account held — `ControlTakenOver`.</summary>
/// <param name="RecipientUserId">Who is being told: the controller who was displaced.</param>
/// <param name="DeviceId">The device.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <param name="ControllerUserId">Who holds it now.</param>
/// <remarks>
/// A separate event from <see cref="ControlGranted"/> because "you have control" and "you no longer have
/// control" are different facts, and neither should have to be inferred from the other's absence. It is sent only
/// when a grant replaced an existing grant — SPEC-022 decision 2.
/// </remarks>
public sealed record ControlTakenOver(
    Guid RecipientUserId,
    Guid DeviceId,
    DateTime OccurredAtUtc,
    Guid ControllerUserId) : ControlBroadcast(RecipientUserId, DeviceId, OccurredAtUtc);
