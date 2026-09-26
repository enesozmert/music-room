using MusicRoom.Domain.Common;

namespace MusicRoom.Domain.Entities;

/// <summary>
/// An account. Uniqueness of <see cref="Email"/> is enforced by the <c>ux_users_email</c> index,
/// not by a read-then-write check in C# — SDD §8.2.
/// </summary>
public sealed class User : Entity
{
    /// <summary>The address the account is known by, stored lower-cased so the unique index is
    /// case-insensitive without a functional index.</summary>
    public required string Email { get; set; }

    /// <summary>The full BCrypt string: algorithm, cost, salt and digest in one column — ADR-0007.
    /// The password itself is never stored, logged or returned.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>The name other users see.</summary>
    public required string DisplayName { get; set; }

    /// <summary>False until the address is proven. An unverified account cannot use the services —
    /// SDD §11.1.</summary>
    public bool IsEmailVerified { get; set; }
}
