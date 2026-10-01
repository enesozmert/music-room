using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// One account's single use of one event code — ADR-0014.
/// </summary>
/// <remarks>
/// Unique per code and account, which is what makes "single use per account" a fact the database holds rather
/// than a check the code performs. Two parallel votes carrying the same code therefore cannot both spend it:
/// the second insert violates the index and the vote is refused.
/// </remarks>
public sealed class EventCodeRedemption : Entity
{
    /// <summary>The code that was spent.</summary>
    public Guid EventCodeId { get; set; }

    /// <summary>Who spent it.</summary>
    public Guid UserId { get; set; }
}
