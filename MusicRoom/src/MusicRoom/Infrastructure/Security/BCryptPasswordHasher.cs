using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Infrastructure.Security;

/// <summary>
/// BCrypt, via <c>BCrypt.Net-Next</c> — ADR-0007.
/// </summary>
/// <remarks>
/// The salt is generated per password by the library, and the cost is stored inside the hash
/// string, so a later increase can be applied on login without a migration.
/// </remarks>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    /// <summary>The floor ADR-0007 sets. Configuration may raise the cost, never lower it.</summary>
    public const int MinimumWorkFactor = 12;

    private readonly int _workFactor;

    /// <summary>Takes the configured cost and clamps it up to the ADR-0007 floor.</summary>
    /// <param name="workFactor">The cost from configuration.</param>
    public BCryptPasswordHasher(int workFactor) =>
        _workFactor = Math.Max(MinimumWorkFactor, workFactor);

    /// <inheritdoc />
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    /// <inheritdoc />
    public bool Verify(string password, string passwordHash) =>
        BCrypt.Net.BCrypt.Verify(password, passwordHash);
}
