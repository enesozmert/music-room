using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One person on an event's roster — subject V.2.1.
/// </summary>
/// <remarks>
/// A roster, **not a gate**. Subject V.2.1 says that when an event is public, all users can find it and vote;
/// requiring an attendance row first would narrow that to the users who remembered to check in. Nothing in
/// this project reads this row to decide whether somebody may act — SPEC-017 decision 5.
/// </remarks>
public sealed class EventAttendance : Entity
{
    /// <summary>The event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Who is attending.</summary>
    public Guid UserId { get; set; }
}
