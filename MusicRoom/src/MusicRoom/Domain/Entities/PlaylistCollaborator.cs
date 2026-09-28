using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// Somebody other than the owner who is named on a playlist — subject V.2.3.
/// </summary>
/// <remarks>
/// A collaborator row is the read gate; <see cref="CanEdit"/> is the write gate. The two are separate on
/// purpose: "you may see this list" and "you may change it" are different grants, and the contract's `member`
/// and `licence` symbols distinguish them.
/// </remarks>
public sealed class PlaylistCollaborator : Entity
{
    /// <summary>The playlist.</summary>
    public Guid PlaylistId { get; set; }

    /// <summary>The account named on it.</summary>
    public Guid UserId { get; set; }

    /// <summary>Whether they may write to it, not merely read it.</summary>
    public bool CanEdit { get; set; }
}
