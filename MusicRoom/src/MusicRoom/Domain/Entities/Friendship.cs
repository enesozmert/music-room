using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// A symmetric friendship between two accounts — the relationship the `Friends` visibility tier consults.
/// </summary>
/// <remarks>
/// **One row per friendship**, with the pair stored in a canonical order: <see cref="UserAId"/> is always the
/// smaller of the two ids. Symmetry is therefore a property of the data rather than of two rows a bug could
/// desynchronise — there is no state in which A is a friend of B while B is not a friend of A.
/// <para>
/// The cost is that a list query reads both columns and projects the other side. That is one query written
/// once, against a class of bug that would be found by a user rather than by a test.
/// </para>
/// </remarks>
public sealed class Friendship : Entity
{
    /// <summary>The smaller of the two account ids.</summary>
    public Guid UserAId { get; set; }

    /// <summary>The larger of the two account ids.</summary>
    public Guid UserBId { get; set; }

    /// <summary>
    /// Orders a pair canonically, so the same two accounts always produce the same row.
    /// </summary>
    /// <param name="first">One account.</param>
    /// <param name="second">The other.</param>
    /// <returns>The pair, smaller id first.</returns>
    /// <exception cref="ArgumentException">Both ids are the same account.</exception>
    public static (Guid UserAId, Guid UserBId) Pair(Guid first, Guid second)
    {
        if (first == second)
        {
            throw new ArgumentException("An account cannot be friends with itself.", nameof(first));
        }

        return first.CompareTo(second) < 0 ? (first, second) : (second, first);
    }
}
