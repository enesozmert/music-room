namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>One device, as its owner reads it.</summary>
/// <param name="DeviceId">The device.</param>
/// <param name="Platform">Its platform.</param>
/// <param name="Model">Its model.</param>
/// <param name="AppVersion">The application version it last registered with.</param>
/// <param name="ControllerUserId">Who holds delegated control, or null while the owner does.</param>
public sealed record DeviceSummary(
    Guid DeviceId,
    string Platform,
    string Model,
    string AppVersion,
    Guid? ControllerUserId);

/// <summary>Who controls a device, and the version a claim must send back.</summary>
/// <param name="DeviceId">The device.</param>
/// <param name="OwnerId">Its owner.</param>
/// <param name="ControllerUserId">
/// Who may command playback on it. **Never null here**: with no grant this is the owner, because that is the
/// question the endpoint was asked. The row's own column stays null, which is a different question — SPEC-020
/// decision 2.
/// </param>
/// <param name="IsDelegated">Whether control is held by somebody other than the owner.</param>
/// <param name="Version">The `xmin`, which the endpoint publishes as the `ETag`.</param>
public sealed record DeviceControl(
    Guid DeviceId,
    Guid OwnerId,
    Guid ControllerUserId,
    bool IsDelegated,
    uint Version);

/// <summary>One device somebody else has given this caller control of.</summary>
/// <param name="DeviceId">The device.</param>
/// <param name="OwnerId">Who owns it.</param>
/// <param name="OwnerDisplayName">Their display name, so a client needs no second call.</param>
/// <param name="Platform">Its platform.</param>
/// <param name="Model">Its model.</param>
public sealed record ControlGrantSummary(
    Guid DeviceId,
    Guid OwnerId,
    string OwnerDisplayName,
    string Platform,
    string Model);

/// <summary>What a guarded grant did, and who held control before it.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="PreviousControllerUserId">
/// Who held delegated control before this write, or null when the owner did. Read **inside** the guarded
/// transaction, before the update and under the same `xmin`, so the person to notify cannot be a guess taken
/// from a read a concurrent grant has already invalidated — SPEC-022 decision 3.
/// </param>
public sealed record DeviceGrantResult(DeviceWriteOutcome Outcome, Guid? PreviousControllerUserId)
{
    /// <summary>No such device, or none this caller owns.</summary>
    public static DeviceGrantResult NotFound { get; } = new(DeviceWriteOutcome.NotFound, null);

    /// <summary>A stale `If-Match`: somebody claimed it first.</summary>
    public static DeviceGrantResult Stale { get; } = new(DeviceWriteOutcome.VersionMismatch, null);

    /// <summary>The grantee is not a friend of the owner.</summary>
    public static DeviceGrantResult NotAFriend { get; } = new(DeviceWriteOutcome.NotAFriend, null);
}

/// <summary>What a guarded device write did.</summary>
public enum DeviceWriteOutcome
{
    /// <summary>No such device, or none this caller owns — the endpoint answers `404`.</summary>
    NotFound = 0,

    /// <summary>Written.</summary>
    Written = 1,

    /// <summary>Somebody changed it after the caller read it — the endpoint answers `412`.</summary>
    VersionMismatch = 2,

    /// <summary>This account already registered this device — the endpoint answers `409`.</summary>
    Conflict = 3,

    /// <summary>
    /// The grantee is not a friend of the owner — the endpoint answers `422`. Delegating playback to a stranger
    /// is a feature nobody asked for (SPEC-020 decision 4).
    /// </summary>
    NotAFriend = 4,
}
