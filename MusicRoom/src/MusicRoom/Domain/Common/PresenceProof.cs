namespace MusicRoom.Domain.Common;

/// <summary>
/// What an event with the location-and-time licence requires a voter to prove — SDD §6.2, ADR-0014.
/// </summary>
/// <remarks>
/// One column with three values rather than two booleans, which would allow a fourth state — "neither" — and
/// so a licence that claims to restrict by place and time with nothing to check. SPEC-017 decision 3 refuses
/// that at creation; this enum makes it unrepresentable.
/// <para>
/// Both proofs are evaluated **server-side**. The client supplies evidence; it never decides.
/// </para>
/// </remarks>
public enum PresenceProof
{
    /// <summary>Coordinates inside the event's radius, checked against server time.</summary>
    Geofence = 0,

    /// <summary>A short-lived code the owner shows at the venue.</summary>
    EventCode = 1,

    /// <summary>Both: coordinates inside the radius **and** a valid code.</summary>
    Both = 2,
}
