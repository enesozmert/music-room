using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Application.Abstractions.Persistence;

/// <summary>
/// Events and their roster — <c>docs/api/ENDPOINTS.md</c> §4.
/// </summary>
/// <remarks>
/// Every write is keyed on the caller inside the statement, so an event the caller does not own simply does
/// not match and the endpoint answers `404`. The guarded `PATCH` additionally carries the `xmin` the caller
/// last read, exactly as the playlist aggregate's does — SPEC-014's mechanism, reused rather than reinvented.
/// </remarks>
public interface IEventRepository
{
    /// <summary>Writes a new event.</summary>
    /// <param name="newEvent">The event, already validated.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task CreateAsync(Event newEvent, CancellationToken cancellationToken);

    /// <summary>The three gates of §4, decided in one read.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="nowUtc">Server time, which is what decides the window — ADR-0014.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>What this caller may do.</returns>
    Task<EventAccess> FindAccessAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>What the licence requires of a voter right now — ADR-0014.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="nowUtc">Server time, which decides the window.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The policy and the window, or null when there is no such event.</returns>
    /// <remarks>
    /// Separate from <see cref="FindAccessAsync"/> because the gate and the proof are two different questions:
    /// the gate says whether this caller may vote at all, and this says what they must show. Only the vote path
    /// asks the second one.
    /// </remarks>
    Task<EventPresence?> FindPresenceAsync(
        Guid eventId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>One event, if the caller may see it.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="nowUtc">Server time.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The event, or null when the caller may not see it — which the endpoint answers `404`.</returns>
    Task<EventDetail?> FindAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>A page of events in one scope.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="scope">Mine, invited, or public.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">How many rows, capped by the endpoint.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The page.</returns>
    Task<IReadOnlyList<EventSummary>> ListAsync(
        Guid userId,
        EventScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Changes the metadata, if the caller owns the event and still holds its version.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="metadata">The new name and window.</param>
    /// <param name="expectedVersion">The `xmin` the caller last read.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The outcome the endpoint turns into `200`, `412` or `404`.</returns>
    Task<EventWriteOutcome> TryUpdateAsync(
        Guid eventId,
        Guid ownerId,
        EventMetadata metadata,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Replaces the licence policy. Owner only.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="licence">The visibility, the mode, and ADR-0014's policy.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was updated.</returns>
    Task<bool> TrySetLicenceAsync(
        Guid eventId,
        Guid ownerId,
        EventLicence licence,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes the event. Owner only.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was stamped.</returns>
    Task<bool> TryDeleteAsync(
        Guid eventId,
        Guid ownerId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Invites somebody. Owner only.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="userId">Who to invite.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict from the unique index, or no such event for this owner.</returns>
    Task<RosterWriteOutcome> TryInviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Withdraws an invitation. Owner only.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="userId">Whose invitation to withdraw.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> TryUninviteAsync(
        Guid eventId,
        Guid ownerId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Records that the caller is attending.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="nowUtc">The instant the row is stamped with.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Added, a conflict, or no such event.</returns>
    /// <remarks>
    /// A roster entry and nothing more: nothing reads it to decide whether somebody may act — SPEC-017
    /// decision 5.
    /// </remarks>
    Task<RosterWriteOutcome> TryAttendAsync(
        Guid eventId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Takes the caller off the roster.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> TryLeaveAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Who the owner has invited.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="ownerId">The caller, who must be the owner.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The invitations, or an empty list when the caller does not own the event.</returns>
    Task<IReadOnlyList<InvitationSummary>> ListInvitationsAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken);
}
