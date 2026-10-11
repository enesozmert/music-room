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
    /// <remarks>
    /// **Null for an account that has only ever signed in with a provider** — SPEC-041, subject V.1's social
    /// registration. A sentinel string was rejected: the first thing that compared a password against it would
    /// be comparing against a value somebody chose, and BCrypt would answer in constant time either way.
    /// <para>
    /// Password sign-in for such an account answers the same generic `401` a wrong password answers. Saying
    /// *"this address exists but has no password"* would be an enumeration oracle, which is the thing
    /// `POST /auth/sessions` has always refused to be.
    /// </para>
    /// </remarks>
    public string? PasswordHash { get; set; }

    /// <summary>The name other users see.</summary>
    public required string DisplayName { get; set; }

    /// <summary>False until the address is proven. An unverified account cannot use the services —
    /// SDD §11.1.</summary>
    public bool IsEmailVerified { get; set; }

    /// <summary>
    /// When the welcome message was sent, or null when it has not been — SPEC-052 decision 9.
    /// </summary>
    /// <remarks>
    /// A stamp rather than a flag, because "when" answers "whether" and one more question besides. It is
    /// what makes once mean once: the first sign-in after verification sends the message and writes this,
    /// and every sign-in after that reads it and sends nothing.
    /// </remarks>
    public DateTime? WelcomeSentAtUtc { get; set; }
}
