using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One person the owner invited to a private event — subject V.2.1.
/// </summary>
/// <remarks>
/// The invitation is the gate on a private event, and it is read live on every request rather than copied
/// anywhere: withdrawing one takes effect on the next call, not on the next sign-in.
/// <para>
/// Unique per event and account, enforced by the database. A duplicate is the index's answer, which is what
/// makes the endpoint's `409` a fact rather than a read-then-write guess.
/// </para>
/// </remarks>
public sealed class EventInvitation : Entity
{
    /// <summary>The event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Who was invited.</summary>
    public Guid UserId { get; set; }
}
