using System.Globalization;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// Proves the ADR-0007 storage properties against the real library.
/// </summary>
public sealed class BCryptPasswordHasherTests
{
    internal const string Password = "correct horse battery";

    /// <summary>
    /// The cost is read back out of the hash string, so the assertion is on what was stored rather
    /// than on what the constructor was told.
    /// </summary>
    [Fact]
    public void Hash_UsesWorkFactorOfAtLeast12()
    {
        // Arrange — configuration asks for a cost below the floor.
        BCryptPasswordHasher hasher = new(4);

        // Act
        string hash = hasher.Hash(Password);

        // Assert — "$2a$12$..." : the cost is the third dollar-separated field.
        string[] parts = hash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        int workFactor = int.Parse(parts[1], CultureInfo.InvariantCulture);

        Assert.True(
            workFactor >= BCryptPasswordHasher.MinimumWorkFactor,
            $"stored work factor {workFactor} is below the ADR-0007 floor");
    }

    /// <summary>The same password hashed twice differs: the salt is per password, never reused.</summary>
    [Fact]
    public void Hash_ProducesADifferentHashForTheSamePassword()
    {
        // Arrange
        BCryptPasswordHasher hasher = new(BCryptPasswordHasher.MinimumWorkFactor);

        // Act
        string first = hasher.Hash(Password);
        string second = hasher.Hash(Password);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>The hash is usable for login: the original verifies, anything else does not.</summary>
    [Fact]
    public void Verify_AcceptsTheOriginalAndRejectsAnyOther()
    {
        // Arrange
        BCryptPasswordHasher hasher = new(BCryptPasswordHasher.MinimumWorkFactor);
        string hash = hasher.Hash(Password);

        // Act
        bool original = hasher.Verify(Password, hash);
        bool other = hasher.Verify("correct horse batteries", hash);

        // Assert
        Assert.True(original);
        Assert.False(other);
    }
}
