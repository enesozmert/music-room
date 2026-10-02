using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// Devices and their control grants — <c>docs/api/ENDPOINTS.md</c> §6.
/// </summary>
/// <remarks>
/// Every write is keyed on the caller inside the statement, so a device the caller does not own simply does not
/// match. The claim additionally carries the `xmin` the caller last read, which is what makes two simultaneous
/// claims resolve to one `200` and one `412` instead of a silent overwrite.
/// </remarks>
public interface IDeviceRepository
{
    /// <summary>Registers a device, or reports that this account already has it.</summary>
    /// <param name="device">The device, already validated.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Registered, or a conflict from the unique index.</returns>
    Task<DeviceWriteOutcome> TryRegisterAsync(Device device, CancellationToken cancellationToken);

    /// <summary>The caller's own devices.</summary>
    /// <param name="ownerId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The devices, newest first.</returns>
    Task<IReadOnlyList<DeviceSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the caller's own devices.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> TryRemoveAsync(Guid deviceId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Who controls a device, for a caller allowed to ask.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="userId">The caller: the owner, or the current controller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The control state and its version, or null when the caller may not ask.</returns>
    Task<DeviceControl?> FindControlAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Hands control to another account, if the caller owns the device and still holds its version.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="granteeUserId">Who is being given control.</param>
    /// <param name="expectedVersion">The `xmin` the caller last read.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `200`, `412` or `404`.</returns>
    /// <remarks>
    /// The token is part of the predicate, so two simultaneous claims are resolved by the database under its own
    /// row lock: one writes, the other matches nothing and is told `412`. The loser loses visibly — SPEC-020
    /// decision 5.
    /// </remarks>
    Task<DeviceWriteOutcome> TryGrantControlAsync(
        Guid deviceId,
        Guid ownerId,
        Guid granteeUserId,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Returns control to the owner, for the owner or the current controller.</summary>
    /// <param name="deviceId">The device.</param>
    /// <param name="userId">The caller: the owner revoking, or the controller releasing.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was updated.</returns>
    /// <remarks>
    /// Both intents have the same effect, so they are one statement with both callers in its predicate — one
    /// route and one way to write the state (SPEC-020 decision 6).
    /// </remarks>
    Task<bool> TryReleaseControlAsync(Guid deviceId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The devices this caller has been given control of.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The grants the caller holds.</returns>
    Task<IReadOnlyList<ControlGrantSummary>> ListGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
