using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A short-lived code the owner shows at the venue — ADR-0014's second presence proof.
/// </summary>
/// <remarks>
/// Credential-shaped, and treated as one: only the SHA-256 digest is stored, the TTL is bounded, issuing it is
/// rate limited, and neither the code nor any part of it is ever logged. A stolen code votes, which is the
/// whole reason the plaintext does not exist in the database — SPEC-018 decision 4.
/// <para>
/// Single use **per account**, not globally: one code is shown to a whole room and must work for everybody in
/// it exactly once. The redemption rows are what enforce that.
/// </para>
/// </remarks>
public sealed class EventCode : Entity
{
    /// <summary>The event it admits people to.</summary>
    public Guid EventId { get; set; }

    /// <summary>Hex-encoded SHA-256 of the code. The plaintext is shown once and never stored.</summary>
    public required string CodeHash { get; set; }

    /// <summary>When it stops being accepted. Always UTC; server time decides.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>The owner who issued it.</summary>
    public Guid IssuedByUserId { get; set; }
}
