namespace MusicRoom.Domain.Common;

/// <summary>
/// The columns every table carries — SDD §8.3.
/// </summary>
/// <remarks>
/// This base lives in <c>Domain</c>, not in <c>Core.Persistence</c> where the reference layout puts
/// it: identity and creation time are domain facts, and <c>Domain</c> references nothing.
/// The decision and its cost are recorded in <c>brain/decisions/ADR-0028-domain-owns-its-base-types.md</c>.
/// </remarks>
public abstract class Entity
{
    /// <summary>The surrogate primary key, mapped to the <c>id</c> column.</summary>
    public Guid Id { get; set; }

    /// <summary>When the row was first written. Always UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the row last changed. Always UTC.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
